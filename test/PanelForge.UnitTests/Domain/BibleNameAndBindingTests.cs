using FluentAssertions;
using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Domain;

public class BibleNameAndBindingTests
{
    [Fact]
    public void BibleNameNormalizer_ShouldTrimCollapseSpacesAndLower()
    {
        BibleNameNormalizer.Normalize("   Hắc   Long   ").Should().Be("hắc long");
        BibleNameNormalizer.Normalize("Naruto   Uzumaki").Should().Be("naruto uzumaki");
        BibleNameNormalizer.Normalize(null).Should().Be(string.Empty);
        BibleNameNormalizer.Normalize("").Should().Be(string.Empty);
        BibleNameNormalizer.Normalize("   ").Should().Be(string.Empty);
    }

    [Fact]
    public void CreateStub_ShouldHaveEmptyDescriptionAndIsStubTrue()
    {
        var seriesBibleId = Guid.NewGuid();
        var stub = BibleEntry.CreateStub(seriesBibleId, BibleEntryCategory.Character, "Kuro");

        stub.IsStub.Should().BeTrue();
        stub.Name.Should().Be("Kuro");
        stub.Description.Should().BeEmpty();
        stub.Code.Should().StartWith("STUB_");
    }

    [Fact]
    public void SetAliases_ShouldValidateAndDeduplicate()
    {
        var entry = BibleEntry.Create(Guid.NewGuid(), BibleEntryCategory.Character, "CHR-01", "Hắc Long", "Mô tả");

        entry.SetAliases(["Dark Dragon", "  dark   dragon  ", "Black Dragon"]);

        entry.Aliases.Should().HaveCount(2);
        entry.Aliases.Should().Contain("Dark Dragon");
        entry.Aliases.Should().Contain("Black Dragon");
    }

    [Fact]
    public void SetAliases_DuplicateOfPrimaryName_ShouldThrow()
    {
        var entry = BibleEntry.Create(Guid.NewGuid(), BibleEntryCategory.Character, "CHR-01", "Hắc Long", "Mô tả");

        var act = () => entry.SetAliases(["hắc   long"]);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*trùng với tên chính*");
    }

    [Fact]
    public void SetAliases_MoreThan20_ShouldThrow()
    {
        var entry = BibleEntry.Create(Guid.NewGuid(), BibleEntryCategory.Character, "CHR-01", "Hắc Long", "Mô tả");
        var list = Enumerable.Range(1, 21).Select(i => $"Alias_{i}").ToList();

        var act = () => entry.SetAliases(list);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*tối đa là 20*");
    }

    [Fact]
    public void SetAliases_MoreThan100Chars_ShouldThrow()
    {
        var entry = BibleEntry.Create(Guid.NewGuid(), BibleEntryCategory.Character, "CHR-01", "Hắc Long", "Mô tả");
        var longAlias = new string('x', 101);

        var act = () => entry.SetAliases([longAlias]);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*100 ký tự*");
    }

    [Fact]
    public void SetCrowd_ShouldToggleFlag()
    {
        var entry = BibleEntry.Create(Guid.NewGuid(), BibleEntryCategory.Character, "CHR-01", "Dân làng A", "Mô tả");
        entry.IsCrowd.Should().BeFalse();

        entry.SetCrowd(true);
        entry.IsCrowd.Should().BeTrue();

        entry.SetCrowd(false);
        entry.IsCrowd.Should().BeFalse();
    }

    [Fact]
    public void ChapterBibleBinding_CreateAndUpdate_ShouldWorkCorrectly()
    {
        var chapterId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var binding = ChapterBibleBinding.Create(
            chapterId: chapterId,
            bibleEntryId: entryId,
            boundRevisionId: revisionId,
            source: ChapterBibleBindingSource.Speaker,
            chosenByWriter: true,
            isCrowdAtBind: false,
            boundById: userId
        );

        binding.ChapterId.Should().Be(chapterId);
        binding.BibleEntryId.Should().Be(entryId);
        binding.BoundRevisionId.Should().Be(revisionId);
        binding.Source.Should().Be(ChapterBibleBindingSource.Speaker);
        binding.ChosenByWriter.Should().BeTrue();
        binding.IsCrowdAtBind.Should().BeFalse();
        binding.BoundById.Should().Be(userId);

        var newRevisionId = Guid.NewGuid();
        binding.UpdateBinding(newRevisionId, ChapterBibleBindingSource.Highlight, chosenByWriter: false);

        binding.BoundRevisionId.Should().Be(newRevisionId);
        binding.Source.Should().Be(ChapterBibleBindingSource.Highlight);
        binding.ChosenByWriter.Should().BeFalse();

        binding.SetCrowdAtBind(true);
        binding.IsCrowdAtBind.Should().BeTrue();
    }

    [Fact]
    public void PanelBibleVariant_CreateAndUpdate_ShouldWorkCorrectly()
    {
        var panelId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var variant = PanelBibleVariant.Create(panelId, entryId, revisionId, userId);

        variant.PanelId.Should().Be(panelId);
        variant.BibleEntryId.Should().Be(entryId);
        variant.RevisionId.Should().Be(revisionId);
        variant.SetById.Should().Be(userId);

        var newRevId = Guid.NewGuid();
        var newUser = Guid.NewGuid();
        variant.UpdateRevision(newRevId, newUser);

        variant.RevisionId.Should().Be(newRevId);
        variant.SetById.Should().Be(newUser);
    }

    [Fact]
    public void BibleEntryName_Create_ShouldNormalizeAndValidate()
    {
        var seriesBibleId = Guid.NewGuid();
        var entryId = Guid.NewGuid();

        var name = BibleEntryName.Create(seriesBibleId, "   Hắc   Long   ", entryId, isPrimary: true);

        name.SeriesBibleId.Should().Be(seriesBibleId);
        name.NormalizedName.Should().Be("hắc long");
        name.BibleEntryId.Should().Be(entryId);
        name.IsPrimary.Should().BeTrue();

        var act = () => BibleEntryName.Create(seriesBibleId, "   ", entryId, isPrimary: false);
        act.Should().Throw<ArgumentException>();
    }
}
