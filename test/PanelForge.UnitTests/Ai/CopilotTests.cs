using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using PanelForge.AI.Agents;
using PanelForge.AI.Contracts;
using PanelForge.AI.Demo;
using PanelForge.AI.Gateway;
using PanelForge.Application.Common;
using PanelForge.Application.Features.AiAgents.Commands.RunCopilot;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence.Seeders;

namespace PanelForge.UnitTests.Ai;

/// <summary>Copilot sync: guard BR-07/BR-03/BR-12/assignment/quota trước khi gọi AI, output là AiSuggestion chờ người Accept (BR-02).</summary>
public sealed class CopilotTests
{
    private const string LongLine = "Bà Lan ơi, con đã dọn xong cả tiệm đồng hồ rồi mà vẫn chưa thấy chiếc chìa khóa đồng đâu cả.";

    private static async Task<(AiHarness H, Guid LineId)> SetupAsync()
    {
        var h = await AiHarness.CreateAsync();
        // AI service thật (CopilotAgent + MockChatClient) chạy in-process thay cho HTTP
        h.AiClient.Setup(c => c.RunCopilotAsync(It.IsAny<CopilotRequest>(), It.IsAny<CancellationToken>()))
            .Returns((CopilotRequest r, CancellationToken ct) =>
                new CopilotAgent(new AgentRunner(new MockChatClient(), Options.Create(new ProviderOptions()))).RunAsync(r, ct));
        var line = ScriptLine.Create(AiDemoSeeder.SceneId, 99, LongLine);
        h.Db.ScriptLines.Add(line);
        await h.Db.SaveChangesAsync();
        return (h, line.Id);
    }

    private static RunCopilotCommandHandler Handler(AiHarness h) =>
        new(h.Db, h.Auth, h.Usage.Object, h.KeyProtector.Object, h.AiClient.Object, h.Settings);

    private static RunCopilotCommand Shorten(Guid lineId, Guid? user = null, int maxChars = 40) =>
        new(AiDemoSeeder.SeriesId, user ?? AiHarness.Letterer, CopilotTasks.LettererShorten, lineId, null, maxChars);

    [Fact]
    public async Task Shorten_CreatesPendingSuggestion_ThenAcceptWritesNewScriptRevision()
    {
        var (h, lineId) = await SetupAsync();

        var result = await Handler(h).Handle(Shorten(lineId), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.Kind.Should().Be(AiSuggestionKind.LineRewrite);
        result.Value.Status.Should().Be(AiSuggestionStatus.Pending);
        (await h.Db.ScriptLines.SingleAsync(l => l.Id == lineId)).DialogueText.Should().Be(LongLine, "AI không ghi thẳng vào script");
        (await h.RunAsync(result.Value.RunId)).Status.Should().Be(AiRunStatus.Completed);

        var accept = await h.ReviewAsync(result.Value.Id, AiSuggestionStatus.Accepted, AiHarness.Letterer, chapterVersion: 0);

        accept.IsSuccess.Should().BeTrue(accept.ErrorMessage);
        var text = (await h.Db.ScriptLines.SingleAsync(l => l.Id == lineId)).DialogueText!;
        text.Length.Should().BeLessThanOrEqualTo(40);
        (await h.Db.ContentRevisions.CountAsync(r => r.SceneId == AiDemoSeeder.SceneId && r.Kind == ContentRevisionKind.Script))
            .Should().Be(2, "baseline + revision mới (append-only)");
    }

    [Fact]
    public async Task WriterRewrite_AllowedForWriter_ForbiddenForLetterer()
    {
        var (h, lineId) = await SetupAsync();
        var cmd = new RunCopilotCommand(AiDemoSeeder.SeriesId, AiHarness.Writer, CopilotTasks.WriterRewrite, lineId, "giọng thân mật", null);

        (await Handler(h).Handle(cmd, default)).IsSuccess.Should().BeTrue();
        (await Handler(h).Handle(cmd with { UserId = AiHarness.Letterer }, default)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }

    [Fact]
    public async Task WrongRole_LockedChapter_AndOtherAssignee_AreBlockedBeforeCallingAi()
    {
        var (h, lineId) = await SetupAsync();

        (await Handler(h).Handle(Shorten(lineId, AiHarness.Writer), default)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);

        var a = Assignment.Create(AiDemoSeeder.SeriesId, AiDemoSeeder.ChapterId, WorkflowEntityType.Page, Guid.NewGuid(),
            Guid.NewGuid(), AiHarness.Editor, WorkspaceRole.Letterer, "Việc Letterer giao người khác");
        h.Db.Assignments.Add(a);
        await h.Db.SaveChangesAsync();
        (await Handler(h).Handle(Shorten(lineId), default)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        h.Db.Assignments.Remove(a);

        var chapter = await h.Db.Chapters.SingleAsync();
        typeof(Chapter).GetProperty(nameof(Chapter.Status))!.SetValue(chapter, ChapterStatus.Locked);
        await h.Db.SaveChangesAsync();
        (await Handler(h).Handle(Shorten(lineId), default)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        h.AiClient.Verify(c => c.RunCopilotAsync(It.IsAny<CopilotRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LineFromAnotherSeries_IsNotFound_AndShortenRequiresMaxChars()
    {
        var (h, lineId) = await SetupAsync();

        (await Handler(h).Handle(Shorten(lineId) with { SeriesId = Guid.NewGuid() }, default)).ErrorCode
            .Should().Be(ResultErrorCodes.NotFound);
        (await Handler(h).Handle(Shorten(lineId) with { MaxChars = null }, default)).ErrorCode
            .Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task PerSeriesRateLimit_ReturnsQuotaExceeded()
    {
        var (h, lineId) = await SetupAsync();
        h.Settings.CopilotCallsPerMinutePerSeries = 1;

        (await Handler(h).Handle(Shorten(lineId), default)).IsSuccess.Should().BeTrue();
        (await Handler(h).Handle(Shorten(lineId), default)).ErrorCode.Should().Be(ResultErrorCodes.AiQuotaExceeded);
    }

    [Fact]
    public async Task AiOutputOverLimit_IsRejected_NoSuggestionStored()
    {
        var (h, lineId) = await SetupAsync();
        h.AiClient.Setup(c => c.RunCopilotAsync(It.IsAny<CopilotRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopilotResponse(true, new string('x', 60), "", "p@1", "p", null, null, 0,
                DateTime.UtcNow, DateTime.UtcNow, null, null));

        var result = await Handler(h).Handle(Shorten(lineId), default);

        result.IsSuccess.Should().BeFalse();
        (await h.Db.AiSuggestions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task LockGate_ListsBlockersAndPendingAiWarning_WithoutLlm()
    {
        var (h, lineId) = await SetupAsync();
        (await Handler(h).Handle(Shorten(lineId), default)).IsSuccess.Should().BeTrue();

        var gate = await new PanelForge.Application.Features.Chapters.Queries.GetChapterLockGate.GetChapterLockGateQueryHandler(h.Db)
            .Handle(new(AiDemoSeeder.ChapterId), default);

        gate.Value!.CanApprove.Should().BeFalse();
        gate.Value.Items.Should().Contain(i => i.Code == "NO_PAGES" && i.Blocking);
        gate.Value.Items.Should().Contain(i => i.Code == "PENDING_AI_SUGGESTIONS" && !i.Blocking && i.Count == 1);
    }

    private static PanelForge.Application.Features.AiAgents.Commands.RunPublisherMetadata.RunPublisherMetadataCommandHandler MetaHandler(AiHarness h) =>
        new(h.Db, h.Auth, h.Usage.Object, h.KeyProtector.Object, h.AiClient.Object, h.Settings);

    private static PanelForge.Application.Features.AiAgents.Commands.RunPublisherMetadata.RunPublisherMetadataCommand Meta(Guid user) =>
        new(AiDemoSeeder.SeriesId, AiDemoSeeder.ChapterId, user, null);

    [Fact]
    public async Task PublisherMetadata_ProducerAllowed_WriterForbidden_AcceptUpdatesSeriesSynopsisAndGenre()
    {
        var (h, _) = await SetupAsync();

        (await MetaHandler(h).Handle(Meta(AiHarness.Writer), default)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);

        var result = await MetaHandler(h).Handle(Meta(AiHarness.Producer), default);
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.Kind.Should().Be(AiSuggestionKind.SeriesMetadata);
        var before = await h.Db.Series.AsNoTracking().SingleAsync(s => s.Id == AiDemoSeeder.SeriesId);

        var accept = await h.ReviewAsync(result.Value.Id, AiSuggestionStatus.Accepted, AiHarness.Producer, chapterVersion: null);

        accept.IsSuccess.Should().BeTrue(accept.ErrorMessage);
        var after = await h.Db.Series.AsNoTracking().SingleAsync(s => s.Id == AiDemoSeeder.SeriesId);
        after.Genre.Should().Be("Mystery, Drama");
        after.Synopsis.Should().NotBe(before.Synopsis);
        (await h.Db.Chapters.SingleAsync()).VersionVector.Should().Be(0, "metadata Series không đổi nội dung chương");
    }

    [Fact]
    public async Task PublisherMetadata_Accept_ConflictsWhenSeriesEditedAfterAiRan()
    {
        var (h, _) = await SetupAsync();
        var result = await MetaHandler(h).Handle(Meta(AiHarness.Producer), default);
        var series = await h.Db.Series.SingleAsync(s => s.Id == AiDemoSeeder.SeriesId);
        series.UpdateDetails(series.Title, "Người dùng vừa sửa tay", series.ReadingDirection);
        await h.Db.SaveChangesAsync();

        var accept = await h.ReviewAsync(result.Value!.Id, AiSuggestionStatus.Accepted, AiHarness.Producer, chapterVersion: null);

        accept.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Fact]
    public void Rules_MetadataAgeRatingAndLengths()
    {
        CopilotRules.ValidateMetadata(new ReleaseMetadata("Log", "Syn", "Drama", "Teaser", "13+")).Should().BeEmpty();
        CopilotRules.ValidateMetadata(new ReleaseMetadata("Log", "Syn", "Drama", "Teaser", "PG")).Should().NotBeEmpty();
        CopilotRules.ValidateMetadata(new ReleaseMetadata("Log", "Syn", new string('g', 101), "Teaser", "All")).Should().NotBeEmpty();
    }

    [Fact]
    public void Rules_ShortenMustBeShorterAndWithinLimit()
    {
        CopilotRules.Validate(CopilotTasks.LettererShorten, "abcdef", "abc", 5).Should().BeEmpty();
        CopilotRules.Validate(CopilotTasks.LettererShorten, "abcdef", "abcdefg", 10).Should().NotBeEmpty();
        CopilotRules.Validate(CopilotTasks.LettererShorten, "abcdef", "abcd", 3).Should().NotBeEmpty();
        CopilotRules.Validate(CopilotTasks.WriterRewrite, "abc", "abc", null).Should().NotBeEmpty();
    }
}
