using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Chapters.Commands.ReopenChapter;
using PanelForge.Application.Features.PreviewLinks.Commands;
using PanelForge.Application.Features.PreviewLinks.Common;
using PanelForge.Application.Features.PreviewLinks.Queries;
using PanelForge.Domain.Entities.Assets;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Preview;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.PreviewLinks;

/// <summary>CF5 UC-11 / UC-21 / UC-24: link xem thử theo email, có hạn, thu hồi được, ghi log mọi lần truy cập (BR-09, BR-17).</summary>
public class PreviewLinkTests
{
    /// <summary>Signer giả: token = "assetRefId|linkId".</summary>
    private sealed class FakeSigner : IAssetLinkSigner
    {
        public string Create(Guid assetReferenceId, Guid userId, TimeSpan lifetime) => $"{assetReferenceId}|{userId}";

        public AssetLinkClaims? Validate(string token)
        {
            var parts = token.Split('|');
            return parts.Length == 2 && Guid.TryParse(parts[0], out var a) && Guid.TryParse(parts[1], out var u)
                ? new AssetLinkClaims(a, u) : null;
        }
    }

    private sealed class Fixture
    {
        public readonly PanelForgeDbContext Db = InMemoryDb.Create();
        public readonly FakeSigner Signer = new();
        public readonly Guid ProducerId = Guid.NewGuid();
        public readonly Chapter Chapter;
        public readonly Page Page1;
        public readonly Page Page2;
        public readonly Panel Panel1;
        public readonly AssetReference InkV1;
        public readonly AssetReference InkV2;
        public readonly AssetReference Pencil;

        public Fixture(bool approve = true)
        {
            var studio = StudioWorkspace.Create("Studio", ProducerId);
            Db.StudioWorkspaces.Add(studio);
            Db.WorkspaceMembers.Add(WorkspaceMember.Create(studio.Id, ProducerId, WorkspaceRole.Producer));
            var series = DomainSeries.Create(studio.Id, "Lantern", ReadingDirection.RightToLeft);
            Db.Series.Add(series);
            Chapter = Chapter.Create(series.Id, 1, "Lá thư");
            if (approve) Chapter.Approve();
            Db.Chapters.Add(Chapter);
            Page1 = Page.CreateNew(Chapter.Id, 1, default, 1200, 1800, 300);
            Page2 = Page.CreateNew(Chapter.Id, 2, default, 1200, 1800, 300);
            Db.Pages.AddRange(Page1, Page2);
            Panel1 = Panel.Create(Page1.Id, 1, 1);
            Db.Panels.Add(Panel1);

            var def = PipelineDefinition.Create(studio.Id, "P", seriesId: series.Id);
            var pencil = PipelineStage.Create(def.Id, "Pencil", "pencil", 1, allowedRole: WorkspaceRole.Artist, isInitial: true);
            var ink = PipelineStage.Create(def.Id, "Ink", "ink", 2, allowedRole: WorkspaceRole.Artist);
            Db.PipelineStages.AddRange(pencil, ink);

            AssetReference Ref(Page p, PipelineStage s, int v) =>
                AssetReference.Create(Guid.NewGuid(), studio.Id, Chapter.Id, p.Id, s.Id, v, null, $"p{p.PageNumber}v{v}.png", ProducerId);
            Pencil = Ref(Page1, pencil, 1);
            InkV1 = Ref(Page1, ink, 1);
            InkV2 = Ref(Page1, ink, 2);
            Db.AssetReferences.AddRange(Pencil, InkV1, InkV2, Ref(Page2, pencil, 1));
            Db.SaveChanges();
        }

        public async Task<(Guid Id, string Token)> CreateLink(int? hours = null)
        {
            var r = await new CreatePreviewLinkCommandHandler(Db).Handle(
                new CreatePreviewLinkCommand(Chapter.Id, ProducerId, "Editor@Publisher.com", hours), default);
            r.IsSuccess.Should().BeTrue(r.ErrorMessage);
            return (r.Value!.Id, r.Value.Token);
        }

        public Task<Result<PanelForge.Application.Features.PreviewLinks.Models.PreviewChapterDto>> Open(string? token) =>
            new OpenPreviewCommandHandler(Db, Signer).Handle(new OpenPreviewCommand(token, "1.2.3.4"), default);
    }

    [Fact]
    public async Task Create_OnlyForApprovedChapter_StoresHashNotToken_AndSnapshotsFinalStageLatestVersion()
    {
        var notApproved = new Fixture(approve: false);
        (await new CreatePreviewLinkCommandHandler(notApproved.Db).Handle(
                new CreatePreviewLinkCommand(notApproved.Chapter.Id, notApproved.ProducerId, "a@b.com", null), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        var f = new Fixture();
        var (id, token) = await f.CreateLink();

        var link = f.Db.PreviewLinks.Single(l => l.Id == id);
        link.RecipientEmail.Should().Be("editor@publisher.com");
        link.TokenHash.Should().Be(PreviewLinkRules.Hash(token)).And.NotBe(token);
        link.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(PreviewLinkRules.DefaultHours), TimeSpan.FromMinutes(1));
        // Trang 1: Ink v2 (stage cuối, mới nhất); trang 2: chỉ có Pencil
        link.SnapshotArtworkIds.Should().HaveCount(2).And.Contain(f.InkV2.Id).And.NotContain(f.InkV1.Id);
    }

    [Theory]
    [InlineData("khong-phai-email", null)]
    [InlineData("a@b.com", 0)]
    [InlineData("a@b.com", 721)]
    public async Task Create_InvalidEmailOrDuration_IsValidation(string email, int? hours)
    {
        var f = new Fixture();
        var r = await new CreatePreviewLinkCommandHandler(f.Db).Handle(
            new CreatePreviewLinkCommand(f.Chapter.Id, f.ProducerId, email, hours), default);
        r.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task Open_ValidToken_ReturnsSnapshotWithWatermark_AndLogsAccess()
    {
        var f = new Fixture();
        var (id, token) = await f.CreateLink();

        var r = await f.Open(token);

        r.IsSuccess.Should().BeTrue();
        r.Value!.WatermarkText.Should().Contain("editor@publisher.com");
        r.Value.ReadingDirection.Should().Be(ReadingDirection.RightToLeft);
        r.Value.Pages.Select(p => p.PageNumber).Should().Equal(1, 2);
        r.Value.Pages[0].AssetReferenceId.Should().Be(f.InkV2.Id);
        r.Value.Pages[0].Panels.Should().ContainSingle(p => p.PanelId == f.Panel1.Id);
        r.Value.Pages[0].ImageUrl.Should().StartWith("/api/preview/images?token=");
        f.Db.PreviewLinkAccessLogs.Should().ContainSingle(l => l.PreviewLinkId == id && l.Allowed && l.Action == "Open");
    }

    [Fact]
    public async Task Open_InvalidRevokedOrExpired_IsForbidden_AndEveryAttemptIsLogged()
    {
        var f = new Fixture();
        var (id, token) = await f.CreateLink();

        (await f.Open("sai-token")).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);

        var link = f.Db.PreviewLinks.Single(l => l.Id == id);
        typeof(PreviewLink).GetProperty(nameof(PreviewLink.ExpiresAt))!.SetValue(link, DateTime.UtcNow.AddMinutes(-1));
        await f.Db.SaveChangesAsync();
        (await f.Open(token)).ErrorMessage.Should().Contain("hết hạn");

        typeof(PreviewLink).GetProperty(nameof(PreviewLink.ExpiresAt))!.SetValue(link, DateTime.UtcNow.AddHours(1));
        await f.Db.SaveChangesAsync();
        await new RevokePreviewLinkCommandHandler(f.Db).Handle(new RevokePreviewLinkCommand(f.Chapter.Id, id, f.ProducerId), default);
        (await f.Open(token)).ErrorMessage.Should().Contain("thu hồi");

        f.Db.PreviewLinkAccessLogs.Select(l => l.DenyReason).Should().BeEquivalentTo(new[] { "InvalidToken", "Expired", "Revoked" });
        f.Db.PreviewLinkAccessLogs.Should().OnlyContain(l => !l.Allowed);
    }

    [Fact]
    public async Task Image_OutsideSnapshotOrAfterRevoke_IsForbidden()
    {
        var f = new Fixture();
        var (id, _) = await f.CreateLink();
        var images = new GetPreviewImageQueryHandler(f.Db, f.Signer, new Moq.Mock<IAssetStorage>().Object);

        // Ink v1 không thuộc snapshot (snapshot là v2)
        (await images.Handle(new GetPreviewImageQuery(f.Signer.Create(f.InkV1.Id, id, TimeSpan.Zero), null), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Forbidden);

        await new RevokePreviewLinkCommandHandler(f.Db).Handle(new RevokePreviewLinkCommand(f.Chapter.Id, id, f.ProducerId), default);
        (await images.Handle(new GetPreviewImageQuery(f.Signer.Create(f.InkV2.Id, id, TimeSpan.Zero), null), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Forbidden);

        f.Db.PreviewLinkAccessLogs.Select(l => l.DenyReason).Should().BeEquivalentTo(new[] { "NotInSnapshot", "Revoked" });
    }

    [Fact]
    public async Task Feedback_IsBoundToTheViewedVersion_AndValidatesPageAndPanel()
    {
        var f = new Fixture();
        var (id, token) = await f.CreateLink();
        var handler = new SubmitPreviewFeedbackCommandHandler(f.Db);

        (await handler.Handle(new SubmitPreviewFeedbackCommand(token, "x", Guid.NewGuid(), null, null), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await handler.Handle(new SubmitPreviewFeedbackCommand(token, "x", f.Page2.Id, f.Panel1.Id, null), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await handler.Handle(new SubmitPreviewFeedbackCommand(token, "  ", null, null, null), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Validation);

        var ok = await handler.Handle(new SubmitPreviewFeedbackCommand(token, "Khung 1 tối quá", f.Page1.Id, f.Panel1.Id, null), default);

        ok.IsSuccess.Should().BeTrue();
        ok.Value!.AssetReferenceId.Should().Be(f.InkV2.Id);
        ok.Value.PreviewLinkId.Should().Be(id);
        ok.Value.RecipientEmail.Should().Be("editor@publisher.com");
        ok.Value.Status.Should().Be(PreviewFeedbackStatus.New);
    }

    [Fact]
    public async Task Triage_NeedsChangeRequiresNote()
    {
        var f = new Fixture();
        var (_, token) = await f.CreateLink();
        var fb = (await new SubmitPreviewFeedbackCommandHandler(f.Db).Handle(
            new SubmitPreviewFeedbackCommand(token, "Sai chính tả trang 2", f.Page2.Id, null, null), default)).Value!;
        var triage = new TriagePreviewFeedbackCommandHandler(f.Db);

        (await triage.Handle(new TriagePreviewFeedbackCommand(f.Chapter.Id, fb.Id, f.ProducerId, PreviewFeedbackStatus.NeedsChange, " "), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Validation);
        var ok = await triage.Handle(new TriagePreviewFeedbackCommand(
            f.Chapter.Id, fb.Id, f.ProducerId, PreviewFeedbackStatus.NeedsChange, "Sửa thoại trang 2"), default);
        ok.Value!.Status.Should().Be(PreviewFeedbackStatus.NeedsChange);
    }

    [Fact]
    public async Task ReopenChapter_RevokesActiveLinks_AndListShowsStatusAndCounts()
    {
        var f = new Fixture();
        var (id, token) = await f.CreateLink();
        await f.Open(token);
        await f.Open("sai");

        var reopened = await new ReopenChapterCommandHandler(f.Db).Handle(
            new ReopenChapterCommand(f.Chapter.Id, f.ProducerId, "Cần sửa theo phản hồi"), default);
        reopened.IsSuccess.Should().BeTrue(reopened.ErrorMessage);

        var list = (await new GetPreviewLinksQueryHandler(f.Db).Handle(new GetPreviewLinksQuery(f.Chapter.Id), default)).Value!;
        var dto = list.Single(l => l.Id == id);
        dto.Status.Should().Be("Revoked");
        dto.RevokeReason.Should().Be("Chương được mở lại");
        dto.AccessCount.Should().Be(1);
        (await f.Open(token)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }
}
