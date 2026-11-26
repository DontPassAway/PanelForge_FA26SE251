using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence.Seeders;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>Workflow Script chế độ viết nối: giữ dòng cũ đã gắn Panel, chỉ thêm dòng mới (BR-01 revision append-only, BR-11).</summary>
public class AppendScriptTests
{
    [Fact]
    public async Task Script_OnSceneWithLinkedLines_IsBlocked_UnlessAppend()
    {
        var h = await AiHarness.CreateAsync();
        await h.AcceptScriptAndLayoutAsync();

        var replace = await h.RunHandler().Handle(await h.CommandAsync(AiWorkflowType.Script), default);
        replace.ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        var append = await h.RunHandler().Handle((await h.CommandAsync(AiWorkflowType.Script)) with { AppendScript = true }, default);
        append.IsSuccess.Should().BeTrue(append.ErrorMessage);
    }

    [Fact]
    public async Task AppendScript_OnlyForScriptWorkflow()
    {
        var h = await AiHarness.CreateAsync();
        var result = await h.RunHandler().Handle((await h.CommandAsync(AiWorkflowType.FullScene)) with { AppendScript = true }, default);
        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task AcceptAppend_KeepsLinkedLines_AndAddsNewLinesAfterLast()
    {
        var h = await AiHarness.CreateAsync();
        await h.AcceptScriptAndLayoutAsync();
        var before = await h.Db.ScriptLines.AsNoTracking().Where(l => l.SceneId == AiDemoSeeder.SceneId).OrderBy(l => l.LineOrder).ToListAsync();
        var lastOrder = before.Max(l => l.LineOrder);

        var cmd = (await h.CommandAsync(AiWorkflowType.Script)) with { AppendScript = true };
        var run = await h.RunHandler().Handle(cmd, default);
        run.IsSuccess.Should().BeTrue(run.ErrorMessage);
        await h.ExecuteWorkflowAsync(run.Value!.RunId);
        var suggestion = await h.SuggestionAsync(run.Value.RunId, AiSuggestionKind.Script);
        var draft = AiJson.Deserialize<ScriptDraft>(suggestion.PayloadJson)!;

        var result = await h.ReviewAsync(suggestion.Id, AiSuggestionStatus.Accepted, chapterVersion: cmd.ChapterVersion);
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);

        var after = await h.Db.ScriptLines.Where(l => l.SceneId == AiDemoSeeder.SceneId).OrderBy(l => l.LineOrder).ToListAsync();
        after.Should().HaveCount(before.Count + draft.Lines.Count);
        foreach (var old in before)
            after.Single(l => l.Id == old.Id).PanelId.Should().Be(old.PanelId, "dòng cũ và liên kết Panel giữ nguyên");
        after.Where(l => l.LineOrder > lastOrder).Select(l => l.Id).Should().Equal(draft.Lines.OrderBy(l => l.Order).Select(l => l.LineId));

        var revision = await h.Db.ContentRevisions.Where(r => r.Kind == ContentRevisionKind.Script).OrderByDescending(r => r.VersionNumber).FirstAsync();
        revision.SourceSuggestionId.Should().Be(suggestion.Id);
        AiJson.Deserialize<ScriptDraft>(revision.PayloadJson)!.Lines.Should().HaveCount(after.Count, "revision chứa toàn bộ script sau khi nối");
    }
}
