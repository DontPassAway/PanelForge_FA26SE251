using FluentAssertions;
using PanelForge.AI.Contracts;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>Validator của code cho output của Artist — nhận định của LLM không bao giờ thay thế các kiểm tra này.</summary>
public class NewAgentContentRulesTests
{
    private static readonly Guid Run = Guid.NewGuid(), Scene = Guid.NewGuid(), LayoutRev = Guid.NewGuid();
    private static readonly Guid An = Guid.NewGuid(), BaLan = Guid.NewGuid(), Cafe = Guid.NewGuid();
    private static readonly Guid Panel1 = Guid.NewGuid(), Panel2 = Guid.NewGuid();

    // ── Artist ───────────────────────────────────────────────────────────────
    private static ArtistContext ArtistCtx() => new(LayoutRev,
        [
            new ArtistPanelContext(Panel1, 1, 1, 1, "wide shot", "Tiệm đồng hồ", [An], []),
            new ArtistPanelContext(Panel2, 1, 2, 2, "close-up", "Bà Lan ngoài hiên", [An, BaLan], [])
        ], []);

    private static ArtistPanelPrompt Prompt(Guid panel, int panelNo, params Guid[] refs) =>
        new(panel, 1, panelNo, "Manga panel, ink", refs, null);

    private static ArtistPromptSet Set(params ArtistPanelPrompt[] panels) =>
        new(AiProtocol.SchemaVersion, Run, Scene, LayoutRev, panels, null);

    private static IReadOnlyList<string> CheckArtist(ArtistPromptSet set) =>
        ContentRules.ValidateArtist(set, Run, Scene, ArtistCtx(), new HashSet<Guid> { An, BaLan, Cafe });

    [Fact]
    public void Artist_ValidSet_Passes() =>
        CheckArtist(Set(Prompt(Panel1, 1, An), Prompt(Panel2, 2, An, BaLan, Cafe))).Should().BeEmpty();

    [Fact]
    public void Artist_MissingPanel_UnknownPanel_Duplicate_AreRejected()
    {
        CheckArtist(Set(Prompt(Panel1, 1, An))).Should().Contain(e => e.Contains("chưa có prompt"));
        CheckArtist(Set(Prompt(Panel1, 1, An), Prompt(Panel2, 2, An, BaLan), Prompt(Guid.NewGuid(), 3, An)))
            .Should().Contain(e => e.Contains("không thuộc layout nền"));
        CheckArtist(Set(Prompt(Panel1, 1, An), Prompt(Panel1, 1, An), Prompt(Panel2, 2, An, BaLan)))
            .Should().Contain(e => e.Contains("hơn một prompt"));
    }

    [Fact]
    public void Artist_ReferenceOutsideBible_IsRejected_AndModelCannotPickItsOwnIds()
    {
        var stray = Guid.NewGuid();
        CheckArtist(Set(Prompt(Panel1, 1, An, stray), Prompt(Panel2, 2, An, BaLan)))
            .Should().Contain(e => e.Contains(stray.ToString()) && e.Contains("Bible"));
    }

    [Fact]
    public void Artist_CharacterInPanelMustBeReferenced()
    {
        CheckArtist(Set(Prompt(Panel1, 1, An), Prompt(Panel2, 2, An)))
            .Should().Contain(e => e.Contains("thiếu tham chiếu nhân vật") && e.Contains(BaLan.ToString()));
    }

    [Fact]
    public void Artist_EmptyOrTooLongPrompt_WrongLayoutRevision_AreRejected()
    {
        var longPrompt = Prompt(Panel1, 1, An) with { Prompt = new string('x', ContentRules.MaxPromptLength + 1) };
        CheckArtist(Set(longPrompt, Prompt(Panel2, 2, An, BaLan))).Should().Contain(e => e.Contains("dài quá"));
        CheckArtist(Set(Prompt(Panel1, 1, An) with { Prompt = " " }, Prompt(Panel2, 2, An, BaLan)))
            .Should().Contain(e => e.Contains("thiếu prompt"));
        CheckArtist(Set(Prompt(Panel1, 1, An), Prompt(Panel2, 2, An, BaLan)) with { LayoutRevisionId = Guid.NewGuid() })
            .Should().Contain(e => e.Contains("layoutRevisionId"));
    }

    [Fact]
    public void LetteringLimits_AreEstimates_ThatGrowWithTheBox_AndHaveAFloor()
    {
        LetteringLimits.ForElement(300, 200).Should().BeGreaterThan(LetteringLimits.ForElement(150, 100));
        LetteringLimits.ForElement(10, 10).Should().Be(LetteringLimits.MinChars);
        LetteringLimits.ForPanel(600, 594).Should().BeGreaterThan(LetteringLimits.ForPanel(300, 300));
    }
}
