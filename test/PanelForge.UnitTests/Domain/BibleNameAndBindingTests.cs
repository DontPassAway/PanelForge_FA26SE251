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
    public void BibleEntry_Create_ShouldStoreNormalizedName()
    {
        var seriesBibleId = Guid.NewGuid();
        var entry = BibleEntry.Create(seriesBibleId, BibleEntryCategory.Character, "CHR-01", "  Hắc   Long  ", "Mô tả");

        entry.NormalizedName.Should().Be("hắc long");
    }
}
