using FluentAssertions;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Domain;

public class SeriesBibleTests
{
    [Fact]
    public void SeriesBible_Create_ShouldInitializeCorrectly()
    {
        // Arrange
        var seriesId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        // Act
        var bible = SeriesBible.Create(seriesId, workspaceId, "Neo Tokyo Bible", "Master lore & style guide");

        // Assert
        bible.Should().NotBeNull();
        bible.SeriesId.Should().Be(seriesId);
        bible.WorkspaceId.Should().Be(workspaceId);
        bible.Name.Should().Be("Neo Tokyo Bible");
        bible.Description.Should().Be("Master lore & style guide");
        bible.Version.Should().Be(1);
        bible.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Series_AttachBible_ShouldLinkCorrectly()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var series = Series.Create(workspaceId, "Cyber Hunter", ReadingDirection.RightToLeft);
        var bible = SeriesBible.Create(series.Id, workspaceId, "Cyber Hunter Bible");

        // Act
        series.AttachBible(bible);

        // Assert
        series.Bible.Should().Be(bible);
        series.Bible!.SeriesId.Should().Be(series.Id);
    }

    [Fact]
    public void SeriesBible_AddEntries_All6Categories_ShouldFilterCorrectly()
    {
        // Arrange
        var bible = SeriesBible.Create(Guid.NewGuid(), Guid.NewGuid(), "Master Bible");

        // Act - Add 6 categories
        var character = bible.AddEntry(
            BibleEntryCategory.Character,
            "CHR-01",
            "Ren Kusanagi",
            "Lead cyber warrior",
            subtitle: "Protagonist",
            priority: BiblePriority.StrictCheck,
            strictCheck: true);

        var location = bible.AddEntry(
            BibleEntryCategory.Location,
            "LOC-01",
            "District 9 Sector B",
            "Neon alleyways with rain reflections",
            subtitle: "Slum District");

        var prop = bible.AddEntry(
            BibleEntryCategory.Prop,
            "PROP-01",
            "Plasma Blade Mk-IV",
            "Ceramic hilt with blue energy emitter",
            subtitle: "Primary Weapon");

        var term = bible.AddEntry(
            BibleEntryCategory.Terminology,
            "TERM-01",
            "Synapse Link",
            "Neural communication protocol across cyberware",
            subtitle: "Lore Concept");

        var rule = bible.AddEntry(
            BibleEntryCategory.StyleRule,
            "RULE-01",
            "Screentone Density",
            "Limit screentone layers to 60 lines per inch for print",
            subtitle: "Inking Spec");

        var fact = bible.AddEntry(
            BibleEntryCategory.PlotFact,
            "FACT-01",
            "The Fall of Old Shibuya (Year 2088)",
            "Shibuya was quarantined following the cyber-outbreak",
            subtitle: "Canon Timeline");

        // Assert
        bible.Entries.Should().HaveCount(6);
        bible.Characters.Should().ContainSingle(c => c.Name == "Ren Kusanagi");
        bible.Locations.Should().ContainSingle(l => l.Name == "District 9 Sector B");
        bible.Props.Should().ContainSingle(p => p.Name == "Plasma Blade Mk-IV");
        bible.Terminology.Should().ContainSingle(t => t.Name == "Synapse Link");
        bible.StyleRules.Should().ContainSingle(r => r.Name == "Screentone Density");
        bible.PlotFacts.Should().ContainSingle(f => f.Name == "The Fall of Old Shibuya (Year 2088)");
    }

    [Fact]
    public void BibleEntry_AddRevision_ShouldTrackAppendOnlyProvenance()
    {
        // Arrange
        var bible = SeriesBible.Create(Guid.NewGuid(), Guid.NewGuid(), "Master Bible");
        var character = bible.AddEntry(
            BibleEntryCategory.Character,
            "CHR-01",
            "Ren",
            "Initial character brief");

        // Act - Add revisions
        var authorId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();

        character.AddRevision("Added cybernetic arm spec", "{\"arm\":\"cyber-v2\"}", "hash_abc_123", authorId, chapterId);
        character.AddRevision("Updated hairstyle and scar", "{\"hair\":\"silver\",\"scar\":true}", "hash_def_456", authorId, chapterId);

        // Assert
        character.Revisions.Should().HaveCount(2);
        var rev1 = character.Revisions.First();
        rev1.VersionNumber.Should().Be(1);
        rev1.Summary.Should().Be("Added cybernetic arm spec");
        rev1.ContentHash.Should().Be("hash_abc_123");
        rev1.AuthorId.Should().Be(authorId);
        rev1.AssociatedChapterId.Should().Be(chapterId);

        var rev2 = character.Revisions.Last();
        rev2.VersionNumber.Should().Be(2);
        rev2.Summary.Should().Be("Updated hairstyle and scar");
        rev2.ContentHash.Should().Be("hash_def_456");
    }

    [Fact]
    public void BibleEntry_UpdateDetails_ShouldUpdateFieldsProperly()
    {
        // Arrange
        var entry = BibleEntry.Create(Guid.NewGuid(), BibleEntryCategory.Character, "CHR-01", "Old Name", "Old Desc");

        // Act
        entry.UpdateDetails("New Name", "New Desc", "New Subtitle", "{\"key\":\"val\"}", "https://img.png", BiblePriority.StrictCheck, true);

        // Assert
        entry.Name.Should().Be("New Name");
        entry.Description.Should().Be("New Desc");
        entry.Subtitle.Should().Be("New Subtitle");
        entry.DetailsJson.Should().Be("{\"key\":\"val\"}");
        entry.ReferenceImageUrl.Should().Be("https://img.png");
        entry.Priority.Should().Be(BiblePriority.StrictCheck);
        entry.StrictCheck.Should().BeTrue();
    }
}
