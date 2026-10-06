using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Application.Assignments;

/// <summary>CF2 "Điều kiện nộp": Artist chỉ nộp được khi mỗi trang đã có artwork ở stage này.</summary>
public partial class AssignmentTests
{
    [Fact]
    public async Task Submit_ArtistWithoutUploadedArtwork_IsBlocked_ThenWorksAfterUpload()
    {
        var f = new Fixture();
        f.AddPanel(f.Page, 1);
        var a = f.SeedFor(WorkflowEntityType.Page, f.Page.Id);
        await Start(f, a, f.ArtistId);

        var blocked = await SubmitWithoutArtwork(f, a);

        blocked.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        blocked.ErrorMessage.Should().Contain("artwork");
        a.Status.Should().Be(AssignmentStatus.InProgress);
        f.Submissions.Should().BeEmpty();

        f.UploadArtworkFor(a);
        (await SubmitWithoutArtwork(f, a)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Submit_ArtworkOfAnotherStage_DoesNotCount()
    {
        var f = new Fixture();
        f.AddPanel(f.Page, 1);
        var pencil = f.SeedFor(WorkflowEntityType.Page, f.Page.Id);
        await Start(f, pencil, f.ArtistId);
        var ink = f.SeedFor(WorkflowEntityType.Page, f.Page.Id, f.InkStage);
        f.UploadArtworkFor(ink); // artwork ở stage Ink, không phải Pencil

        var result = await SubmitWithoutArtwork(f, pencil);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Fact]
    public async Task Submit_ArtworkOfAnotherPage_DoesNotCount()
    {
        var f = new Fixture();
        var second = f.AddPage(2);
        f.AddPanel(f.Page, 1);
        f.AddPanel(second, 1);
        var a = f.SeedFor(WorkflowEntityType.Page, f.Page.Id);
        await Start(f, a, f.ArtistId);

        // Artwork chỉ có ở trang khác thì việc của trang 1 vẫn thiếu
        f.AssetReferences.Add(Fixture_Reference(f, second.Id, a.PipelineStageId));
        (await SubmitWithoutArtwork(f, a)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        f.AssetReferences.Add(Fixture_Reference(f, f.Page.Id, a.PipelineStageId));
        (await SubmitWithoutArtwork(f, a)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Resubmit_AfterRejection_NeedsANewerArtworkVersion()
    {
        var f = new Fixture();
        var (a, cr) = await Returned(f);
        await Start(f, a, f.ArtistId);
        await ResolveCr(f, cr, f.ArtistId);

        // Bản artwork cũ (trước lần bị trả về) không đủ để nộp lại
        foreach (var r in f.AssetReferences) r.CreatedAt = f.Submissions.First().DecidedAt!.Value.AddSeconds(-5);
        var blocked = await SubmitWithoutArtwork(f, a);
        blocked.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        blocked.ErrorMessage.Should().Contain("bản tải lên mới");

        f.UploadArtworkFor(a); // phiên bản mới sau lần từ chối
        (await SubmitWithoutArtwork(f, a)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Submit_ChapterLevelAssignment_DoesNotNeedArtwork()
    {
        var f = new Fixture();
        var script = f.SeedFor(WorkflowEntityType.Chapter, f.Chapter.Id, role: WorkspaceRole.Writer);
        var scene = PanelForge.Domain.Entities.Content.Scene.Create(f.Chapter.Id, 1);
        f.Scenes.Add(scene);
        f.Lines.Add(PanelForge.Domain.Entities.Content.ScriptLine.Create(scene.Id, 1, "Xin chào"));
        await Start(f, script, f.ArtistId);

        (await SubmitWithoutArtwork(f, script)).IsSuccess.Should().BeTrue();
    }

    private static PanelForge.Domain.Entities.Assets.AssetReference Fixture_Reference(Fixture f, Guid pageId, Guid stageId) =>
        PanelForge.Domain.Entities.Assets.AssetReference.Create(
            Guid.NewGuid(), f.Series.WorkspaceId, f.Chapter.Id, pageId, stageId,
            f.AssetReferences.Count + 1, null, "art.png", f.ArtistId);
}
