using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using PanelForge.AI.Agents;
using PanelForge.AI.Contracts;
using PanelForge.AI.Demo;
using PanelForge.AI.Gateway;
using PanelForge.Application.Common;
using PanelForge.Application.Features.AiAgents.Commands.RunNextLine;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence.Seeders;

namespace PanelForge.UnitTests.Ai;

/// <summary>
/// TASK-358: Copilot viết dòng tiếp theo. Guard BR-07/BR-03/BR-12 trước khi gọi AI; người nói phải có trong Bible;
/// kết quả là đề xuất ScriptLineInsert chờ người Accept (BR-02); Accept chèn dòng + revision mới (BR-01).
/// </summary>
public sealed class NextLineCopilotTests
{
    private static async Task<(AiHarness H, Guid First, Guid Second)> SetupAsync()
    {
        var h = await AiHarness.CreateAsync();
        h.AiClient.Setup(c => c.RunCopilotAsync(It.IsAny<CopilotRequest>(), It.IsAny<CancellationToken>()))
            .Returns((CopilotRequest r, CancellationToken ct) =>
                new CopilotAgent(new AgentRunner(new MockChatClient(), Options.Create(new ProviderOptions()))).RunAsync(r, ct));
        var first = ScriptLine.Create(AiDemoSeeder.SceneId, 100, kind: ScriptLineKind.Dialogue, speakerLabel: "An",
            text: "Bà Lan ơi, bà có thấy chiếc chìa khóa bạc không?");
        var second = ScriptLine.Create(AiDemoSeeder.SceneId, 101, kind: ScriptLineKind.Action, text: "Bà Lan im lặng rót trà.");
        h.Db.ScriptLines.AddRange(first, second);
        await h.Db.SaveChangesAsync();
        return (h, first.Id, second.Id);
    }

    private static RunNextLineCommandHandler Handler(AiHarness h) =>
        new(h.Db, h.Auth, h.Usage.Object, h.KeyProtector.Object, h.AiClient.Object, h.Settings);

    private static RunNextLineCommand Cmd(string kind, Guid? after, string? speaker = null, Guid? user = null) =>
        new(AiDemoSeeder.SeriesId, user ?? AiHarness.Writer, AiDemoSeeder.SceneId, after, kind, speaker, null);

    [Fact]
    public async Task Dialogue_IsPendingSuggestion_ThenAcceptInsertsAfterAnchor_AndWritesNewRevision()
    {
        var (h, first, second) = await SetupAsync();
        var before = await h.Db.ScriptLines.CountAsync(l => l.SceneId == AiDemoSeeder.SceneId);

        var result = await Handler(h).Handle(Cmd("dialogue", first, speaker: "bà lan"), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.Kind.Should().Be(AiSuggestionKind.ScriptLineInsert);
        result.Value.Status.Should().Be(AiSuggestionStatus.Pending);
        (await h.Db.ScriptLines.CountAsync(l => l.SceneId == AiDemoSeeder.SceneId)).Should().Be(before, "AI không ghi thẳng vào script");
        var sent = (CopilotRequest)h.AiClient.Invocations.Single().Arguments[0];
        sent.NextLine!.AllowedSpeakers.Should().Equal(["Bà Lan"], "Writer đã chọn người nói thì AI không được đổi");
        sent.NextLine.Before.Should().Contain(l => l.StartsWith("An: "));
        sent.NextLine.After.Should().ContainSingle().Which.Should().StartWith("(hành động)");

        var accept = await h.ReviewAsync(result.Value.Id, AiSuggestionStatus.Accepted, AiHarness.Writer, chapterVersion: 0);

        accept.IsSuccess.Should().BeTrue(accept.ErrorMessage);
        var lines = await h.Db.ScriptLines.Where(l => l.SceneId == AiDemoSeeder.SceneId).OrderBy(l => l.LineOrder).ToListAsync();
        lines.Should().HaveCount(before + 1);
        var inserted = lines.Single(l => l.LineOrder == 101);
        inserted.Kind.Should().Be(ScriptLineKind.Dialogue);
        inserted.SpeakerLabel.Should().Be("Bà Lan");
        lines.Single(l => l.Id == second).LineOrder.Should().Be(102, "dòng sau vị trí chèn dời xuống");
        (await h.Db.ContentRevisions.AnyAsync(r => r.SceneId == AiDemoSeeder.SceneId && r.Kind == ContentRevisionKind.Script
            && r.SourceSuggestionId == result.Value.Id)).Should().BeTrue("provenance: revision trỏ về đề xuất AI");
    }

    [Fact]
    public async Task Sfx_WithoutAnchor_IsAppendedAtEndOfScene_WithoutSpeaker()
    {
        var (h, _, second) = await SetupAsync();

        var result = await Handler(h).Handle(Cmd("Sfx", null), default);
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        (await h.ReviewAsync(result.Value!.Id, AiSuggestionStatus.Accepted, AiHarness.Writer, chapterVersion: 0))
            .IsSuccess.Should().BeTrue();

        var last = await h.Db.ScriptLines.Where(l => l.SceneId == AiDemoSeeder.SceneId).OrderBy(l => l.LineOrder).LastAsync();
        last.Kind.Should().Be(ScriptLineKind.Sfx);
        last.SpeakerLabel.Should().BeNull();
        last.LineOrder.Should().Be(102);
        (await h.Db.ScriptLines.SingleAsync(l => l.Id == second)).LineOrder.Should().Be(101);
    }

    [Fact]
    public async Task InvalidInput_WrongRole_AndLockedChapter_AreBlockedBeforeCallingAi()
    {
        var (h, first, _) = await SetupAsync();

        (await Handler(h).Handle(Cmd("Poem", first), default)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await Handler(h).Handle(Cmd("Dialogue", first, speaker: "Người lạ"), default)).ErrorCode
            .Should().Be(ResultErrorCodes.Validation, "người nói phải có trong Series Bible");
        (await Handler(h).Handle(Cmd("Action", first, speaker: "An"), default)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await Handler(h).Handle(Cmd("Action", Guid.NewGuid()), default)).ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        (await Handler(h).Handle(Cmd("Action", first, user: AiHarness.Editor), default)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);

        var chapter = await h.Db.Chapters.SingleAsync();
        typeof(Chapter).GetProperty(nameof(Chapter.Status))!.SetValue(chapter, ChapterStatus.ChapterApproved);
        await h.Db.SaveChangesAsync();
        (await Handler(h).Handle(Cmd("Action", first), default)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        h.AiClient.Verify(c => c.RunCopilotAsync(It.IsAny<CopilotRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AiReturningSpeakerOutsideBible_IsRejected_AndNoSuggestionIsStored()
    {
        var (h, first, _) = await SetupAsync();
        h.AiClient.Setup(c => c.RunCopilotAsync(It.IsAny<CopilotRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopilotResponse(true, "Ta là kẻ lạ mặt.", "x", "1", "ref", null, null, 0,
                DateTime.UtcNow, DateTime.UtcNow, null, null, Speaker: "Kẻ lạ mặt"));

        var result = await Handler(h).Handle(Cmd("Dialogue", first), default);

        result.IsSuccess.Should().BeFalse();
        (await h.Db.AiSuggestions.AnyAsync(s => s.Kind == AiSuggestionKind.ScriptLineInsert)).Should().BeFalse();
    }

    [Fact]
    public async Task Accept_WhenAnchorLineWasDeleted_Conflicts()
    {
        var (h, first, _) = await SetupAsync();
        var result = await Handler(h).Handle(Cmd("Action", first), default);
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);

        h.Db.ScriptLines.Remove(await h.Db.ScriptLines.SingleAsync(l => l.Id == first));
        await h.Db.SaveChangesAsync();

        (await h.ReviewAsync(result.Value!.Id, AiSuggestionStatus.Accepted, AiHarness.Writer, chapterVersion: 0))
            .ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Theory]
    [InlineData("Dialogue", "An", "Xin chào", true)]
    [InlineData("Dialogue", null, "Xin chào", false)]
    [InlineData("Dialogue", "Kael", "Xin chào", false)]
    [InlineData("Sfx", null, "RẦMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMM!", false)]
    [InlineData("Action", "An", "An chạy.", false)]
    [InlineData("Action", null, "", false)]
    public void NextLineRules_Validate(string kind, string? speaker, string text, bool ok) =>
        NextLineRules.Validate(kind, speaker, text, ["An", "Bà Lan"]).Should().HaveCount(ok ? 0 : 1);
}
