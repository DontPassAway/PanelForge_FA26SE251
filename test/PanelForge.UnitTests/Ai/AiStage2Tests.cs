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
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence.Seeders;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>Giai đoạn 2: rút gọn theo bóng chữ khi Frozen, người nói cho Copilot, phản hồi từ duyệt quay lại AI.</summary>
public sealed class AiStage2Tests
{
    private const string LongText = "Bà Lan ơi, con đã dọn xong cả tiệm đồng hồ rồi mà vẫn chưa thấy chiếc chìa khóa đâu cả.";

    private static async Task<AiHarness> HarnessAsync(Action<CopilotRequest>? capture = null)
    {
        var h = await AiHarness.CreateAsync();
        h.AiClient.Setup(c => c.RunCopilotAsync(It.IsAny<CopilotRequest>(), It.IsAny<CancellationToken>()))
            .Returns((CopilotRequest r, CancellationToken ct) =>
            {
                capture?.Invoke(r);
                return new CopilotAgent(new AgentRunner(new MockChatClient(), Options.Create(new ProviderOptions()))).RunAsync(r, ct);
            });
        return h;
    }

    private static RunCopilotCommandHandler Handler(AiHarness h) =>
        new(h.Db, h.Auth, h.Usage.Object, h.KeyProtector.Object, h.AiClient.Object, h.Settings, h.Pages.Object);

    private static async Task<(Page Page, Guid ElementId)> BubbleAsync(AiHarness h)
    {
        await h.AcceptScriptAndLayoutAsync();
        var page = h.SavedPages.Values.First();
        page.AddElement("SPEECH_BALLOON", 10, 10, 300, 200, 1, AiHarness.Letterer, LongText);
        return (page, page.Elements.Keys.Single());
    }

    private static async Task FreezeAsync(AiHarness h)
    {
        var chapter = await h.Db.Chapters.SingleAsync(c => c.Id == AiDemoSeeder.ChapterId);
        h.Db.Entry(chapter).Property(c => c.ScriptStatus).CurrentValue = ScriptStatus.Frozen;
        await h.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task BubbleShorten_WorksWhenFrozen_AndLeavesScriptUntouched()
    {
        var h = await HarnessAsync();
        var (page, elementId) = await BubbleAsync(h);
        await FreezeAsync(h);
        var scriptBefore = await h.Db.ScriptLines.AsNoTracking().Select(l => l.DialogueText).ToListAsync();

        var run = await Handler(h).Handle(new RunCopilotCommand(AiDemoSeeder.SeriesId, AiHarness.Letterer, CopilotTasks.LettererShorten,
            null, null, 30, page.Id, elementId), default);
        run.IsSuccess.Should().BeTrue(run.ErrorMessage);
        run.Value!.Kind.Should().Be(AiSuggestionKind.Lettering);

        (await h.ReviewAsync(run.Value.Id, AiSuggestionStatus.Accepted, AiHarness.Writer, null)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        var accepted = await h.ReviewAsync(run.Value.Id, AiSuggestionStatus.Accepted, AiHarness.Letterer, null);

        accepted.IsSuccess.Should().BeTrue(accepted.ErrorMessage);
        page.Elements[elementId].Content!.Length.Should().BeLessThanOrEqualTo(30);
        (await h.Db.ScriptLines.AsNoTracking().Select(l => l.DialogueText).ToListAsync()).Should().Equal(scriptBefore);
    }

    [Fact]
    public async Task BubbleShorten_IsRejected_WhenBubbleChangedAfterSuggestion()
    {
        var h = await HarnessAsync();
        var (page, elementId) = await BubbleAsync(h);
        var run = await Handler(h).Handle(new RunCopilotCommand(AiDemoSeeder.SeriesId, AiHarness.Letterer, CopilotTasks.LettererShorten,
            null, null, 30, page.Id, elementId), default);
        page.UpdateElementText(elementId, "Chữ mới", AiHarness.Letterer, null, null, null);

        (await h.ReviewAsync(run.Value!.Id, AiSuggestionStatus.Accepted, AiHarness.Letterer, null)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Fact]
    public async Task Copilot_SendsResolvedSpeakerFirst()
    {
        CopilotRequest? sent = null;
        var h = await HarnessAsync(r => sent = r);
        var line = ScriptLine.Create(AiDemoSeeder.SceneId, 99, LongText, speakerLabel: "An");
        h.Db.ScriptLines.Add(line);
        await h.Db.SaveChangesAsync();

        var result = await Handler(h).Handle(new RunCopilotCommand(AiDemoSeeder.SeriesId, AiHarness.Writer, CopilotTasks.WriterRewrite,
            line.Id, null, null), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        sent!.Line!.SpeakerId.Should().NotBeNull();
        sent.Bible[0].EntryId.Should().Be(sent.Line.SpeakerId!.Value);
    }

    [Fact]
    public async Task Rerun_CarriesReviewerFeedback_OnlyFromChangesRequested()
    {
        var h = await HarnessAsync();
        var script = await h.SuggestionAsync(await h.CompleteDemoRunAsync(), AiSuggestionKind.Script);
        var command = h.RunCommand() with { RevisionOfSuggestionId = script.Id };

        (await h.RunHandler().Handle(command, default)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        (await h.ReviewAsync(script.Id, AiSuggestionStatus.ChangesRequested, AiHarness.Writer, null, "Thêm tiếng mưa")).IsSuccess.Should().BeTrue();
        var rerun = await h.RunHandler().Handle(command, default);

        rerun.IsSuccess.Should().BeTrue(rerun.ErrorMessage);
        (await h.RunAsync(rerun.Value!.RunId)).Brief.Should().Contain("Thêm tiếng mưa");
    }
}
