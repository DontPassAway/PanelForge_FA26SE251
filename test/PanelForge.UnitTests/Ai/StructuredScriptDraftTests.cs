using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>Đề xuất Script theo cấu trúc trang script: loại dòng, người nói, sắc thái, tham chiếu Bible.</summary>
public sealed class StructuredScriptDraftTests
{
    private static readonly Guid L = Guid.NewGuid();

    private static IReadOnlyList<string> Validate(ScriptDraftLine line)
    {
        var run = Guid.NewGuid(); var scene = Guid.NewGuid();
        return ContentRules.ValidateScript(new ScriptDraft(AiProtocol.SchemaVersion, run, scene, 0, [line], [], null),
            run, scene, new HashSet<Guid>(), new HashSet<Guid>());
    }

    [Fact]
    public void Rules_RejectBadStructure()
    {
        Validate(new(L, 1, null, null, "Mưa rơi", "Poem")).Should().NotBeEmpty();
        Validate(new(L, 1, null, null, "Mưa rơi", "Action", "An")).Should().NotBeEmpty();
        Validate(new(L, 1, null, "Chào", null, "Dialogue", "An", [new("Chào", Guid.NewGuid())])).Should().NotBeEmpty();
        Validate(new(L, 1, null, null, "Mưa rơi", "Action", null, [new("nắng", Guid.NewGuid())])).Should().NotBeEmpty();
        Validate(new(L, 1, null, null, "Mưa rơi", "Action", null, [new("Mưa", null, "Mưa", "Weather")])).Should().NotBeEmpty();
        Validate(new(L, 1, null, null, "Mưa rơi", "Sfx", null, [new("Mưa", null, "Mưa", "Prop")])).Should().BeEmpty();
    }

    [Fact]
    public async Task Accept_KeepsStructure_AndCreatesStubsOnlyThen()
    {
        var h = await AiHarness.CreateAsync();
        var runId = await h.RunMockAsync(AiWorkflowType.Script);
        var suggestion = await h.SuggestionAsync(runId, AiSuggestionKind.Script);
        var entity = await h.Db.AiSuggestions.SingleAsync(s => s.Id == suggestion.Id);
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var draft = JsonSerializer.Deserialize<ScriptDraft>(entity.PayloadJson, web)!;
        var first = draft.Lines[0];
        var said = draft.Lines.Single(l => l.Kind == "Dialogue");
        said.SpeakerLabel.Should().Be("An (lẩm bẩm)");
        var surface = first.StageDirection!.Split(' ')[0];
        var edited = draft with
        {
            Lines = draft.Lines.Select(l => l.LineId == first.LineId
                ? l with { Kind = "Action", Refs = [new ScriptDraftRef(surface, null, "Mái hiên cũ", "Location")] } : l).ToList()
        };
        var json = JsonSerializer.Serialize(edited, web);
        typeof(AiSuggestion).GetProperty(nameof(AiSuggestion.PayloadJson))!.SetValue(entity, json);
        typeof(AiSuggestion).GetProperty(nameof(AiSuggestion.PayloadHash))!.SetValue(entity, AiContentValidator.Sha256(json));
        await h.Db.SaveChangesAsync();
        (await h.Db.BibleEntries.AnyAsync(e => e.Name == "Mái hiên cũ")).Should().BeFalse();

        var accepted = await h.ReviewAsync(suggestion.Id, AiSuggestionStatus.Accepted, AiHarness.Writer, 0);

        accepted.IsSuccess.Should().BeTrue(accepted.ErrorMessage);
        var line = await h.Db.ScriptLines.SingleAsync(l => l.Id == said.LineId);
        line.Kind.Should().Be(ScriptLineKind.Dialogue);
        line.SpeakerLabel.Should().Be("An (lẩm bẩm)");
        var stub = await h.Db.BibleEntries.SingleAsync(e => e.Name == "Mái hiên cũ");
        stub.IsStub.Should().BeTrue();
        (await h.Db.ScriptLineBibleLinks.AnyAsync(k => k.ScriptLineId == first.LineId && k.BibleEntryId == stub.Id)).Should().BeTrue();
    }
}
