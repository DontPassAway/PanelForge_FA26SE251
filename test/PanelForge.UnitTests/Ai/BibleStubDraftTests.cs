using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using PanelForge.AI.Agents;
using PanelForge.AI.Contracts;
using PanelForge.AI.Demo;
using PanelForge.AI.Gateway;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Commands.RunBibleStubDraft;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence.Seeders;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>Bible Stub Harvester: chỉ dùng dòng có nhắc tới mục, trích dẫn kiểm được, Accept không ghi Bible.</summary>
public sealed class BibleStubDraftTests
{
    private static async Task<(AiHarness H, BibleEntry Entry)> SetupAsync(bool mention = true)
    {
        var h = await AiHarness.CreateAsync();
        h.AiClient.Setup(c => c.RunCopilotAsync(It.IsAny<CopilotRequest>(), It.IsAny<CancellationToken>()))
            .Returns((CopilotRequest r, CancellationToken ct) =>
                new CopilotAgent(new AgentRunner(new MockChatClient(), Options.Create(new ProviderOptions()))).RunAsync(r, ct));
        var entry = await h.Db.BibleEntries.FirstAsync();
        entry.MarkAsStub();
        if (mention)
        {
            var line = ScriptLine.Create(AiDemoSeeder.SceneId, 99, "Chiếc chìa khóa bạc nằm dưới quầy.");
            h.Db.ScriptLines.Add(line);
            h.Db.ScriptLineBibleLinks.Add(ScriptLineBibleLink.Create(line.Id, entry.Id));
        }
        await h.Db.SaveChangesAsync();
        return (h, entry);
    }

    private static RunBibleStubDraftCommandHandler Handler(AiHarness h) =>
        new(h.Db, h.Auth, h.Usage.Object, h.KeyProtector.Object, h.AiClient.Object, h.Settings);

    private static RunBibleStubDraftCommand Command(BibleEntry e, Guid? user = null) =>
        new(AiDemoSeeder.SeriesId, e.Id, user ?? AiHarness.Writer, null);

    [Fact]
    public async Task Draft_HasVerifiableCitation_AndAcceptDoesNotDefineEntry()
    {
        var (h, entry) = await SetupAsync();

        var result = await Handler(h).Handle(Command(entry), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.Kind.Should().Be(AiSuggestionKind.BibleStub);
        (await h.ReviewAsync(result.Value.Id, AiSuggestionStatus.Accepted, AiHarness.Writer, null)).IsSuccess.Should().BeTrue();
        (await h.Db.BibleEntries.SingleAsync(e => e.Id == entry.Id)).IsStub.Should().BeTrue();
    }

    [Fact]
    public async Task NoMention_DoesNotCallAi()
    {
        var (h, entry) = await SetupAsync(mention: false);

        (await Handler(h).Handle(Command(entry), default)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        h.AiClient.Verify(c => c.RunCopilotAsync(It.IsAny<CopilotRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SpeakerStub_UsesLinesTheCharacterSpeaks_AsEvidence()
    {
        // Stub Nhân vật tạo từ tên người nói không có thẻ ScriptLineBibleLink: dòng nhân vật nói vẫn là căn cứ
        var (h, _) = await SetupAsync(mention: false);
        var character = await h.Db.BibleEntries.FirstAsync(e => e.Category == BibleEntryCategory.Character);
        character.MarkAsStub();
        h.Db.ScriptLines.Add(ScriptLine.Create(AiDemoSeeder.SceneId, 98, "Hai ngươi dừng tay lại!",
            kind: ScriptLineKind.Dialogue, speakerLabel: character.Name.ToUpperInvariant()));
        await h.Db.SaveChangesAsync();

        var result = await Handler(h).Handle(Command(character), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        h.AiClient.Verify(c => c.RunCopilotAsync(
            It.Is<CopilotRequest>(r => r.Stub!.Evidence.Any(ev => ev.Text.Contains("Hai ngươi dừng tay lại!"))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnlyWriter_CanRequestDraft()
    {
        var (h, entry) = await SetupAsync();
        (await Handler(h).Handle(Command(entry, AiHarness.Producer), default)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }

    [Fact]
    public void Validation_RejectsInventedQuoteAndUnknownLine()
    {
        var lineId = Guid.NewGuid();
        var context = new BibleStubContext("Chìa khóa", "Prop", [new BibleStubEvidence(lineId, "Chiếc chìa khóa bạc")]);

        CopilotRules.ValidateStubDraft(new BibleStubDraft("Mô tả", null, [new("Bạc", lineId, "chìa khóa bạc")]), context).Should().BeEmpty();
        CopilotRules.ValidateStubDraft(new BibleStubDraft("Mô tả", null, [new("Vàng", lineId, "chìa khóa vàng")]), context).Should().NotBeEmpty();
        CopilotRules.ValidateStubDraft(new BibleStubDraft("Mô tả", null, [new("Bạc", Guid.NewGuid(), "chìa khóa bạc")]), context).Should().NotBeEmpty();
    }
}
