using System.Text.Json;
using FluentAssertions;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Domain;

public class SeriesBibleTests
{
    [Fact]
    public void BibleEntryDto_WithoutCanon_UsesNewestRevision()
    {
        var entry = BibleEntry.Create(Guid.NewGuid(), BibleEntryCategory.Character, "Hero", "Cached");
        entry.AddRevision("v1", JsonSerializer.Serialize(new { Description = "Draft 1" }), "h1", isInitialVersion: true);
        entry.AddRevision("v2", JsonSerializer.Serialize(new { Description = "Draft 2" }), "h2");

        var dto = BibleEntryDto.From(entry);

        dto.Description.Should().Be("Draft 2");
        dto.EffectiveRevision!.VersionNumber.Should().Be(2);
        dto.LatestDraftVersionNumber.Should().BeNull();
    }

    [Fact]
    public void BibleEntryDto_WithCanon_UsesCanonAndReportsNewerDraft()
    {
        var entry = BibleEntry.Create(Guid.NewGuid(), BibleEntryCategory.Character, "Hero", "Cached");
        var canon = entry.AddRevision(
            "v1", JsonSerializer.Serialize(new { Description = "Canon description", ReferenceImageUrl = "canon.png" }),
            "h1", isInitialVersion: true);
        var artistId = Guid.NewGuid();
        canon.SubmitConcept(artistId, "canon-concept.png", "ready");
        canon.ConfirmCanon(Guid.NewGuid(), canon.CurrentConceptEventId!.Value);
        entry.AddRevision("v2", JsonSerializer.Serialize(new { Description = "Draft description" }), "h2");

        var dto = BibleEntryDto.From(entry);

        dto.Description.Should().Be("Canon description");
        dto.ReferenceImageUrl.Should().Be("canon-concept.png");
        dto.EffectiveRevision!.VersionNumber.Should().Be(1);
        dto.LatestDraftVersionNumber.Should().Be(2);
    }

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
            "Ren Kusanagi",
            "Lead cyber warrior");

        var location = bible.AddEntry(
            BibleEntryCategory.Location,
            "District 9 Sector B",
            "Neon alleyways with rain reflections");

        var prop = bible.AddEntry(
            BibleEntryCategory.Prop,
            "Plasma Blade Mk-IV",
            "Ceramic hilt with blue energy emitter");

        var term = bible.AddEntry(
            BibleEntryCategory.Terminology,
            "Synapse Link",
            "Neural communication protocol across cyberware");

        var rule = bible.AddEntry(
            BibleEntryCategory.StyleRule,
            "Screentone Density",
            "Limit screentone layers to 60 lines per inch for print");

        var fact = bible.AddEntry(
            BibleEntryCategory.PlotFact,
            "The Fall of Old Shibuya (Year 2088)",
            "Shibuya was quarantined following the cyber-outbreak");

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
        var entry = BibleEntry.Create(Guid.NewGuid(), BibleEntryCategory.Character, "Old Name", "Old Desc");

        // Act
        entry.UpdateDetails("New Desc", "{\"key\":\"val\"}", "https://img.png");

        // Assert
        entry.Name.Should().Be("Old Name");
        entry.Description.Should().Be("New Desc");
        entry.DetailsJson.Should().Be("{\"key\":\"val\"}");
        entry.ReferenceImageUrl.Should().Be("https://img.png");
    }

    [Fact]
    public void GetDefaultVariantRevision_CanonOnly_UsesLatestLockedDesign_ButNotForTextOnlyCategories()
    {
        var hero = BibleEntry.Create(Guid.NewGuid(), BibleEntryCategory.Character, "Hero", "Desc");
        var v1 = hero.AddRevision("v1", "{}", "h1", isInitialVersion: true);
        hero.AddRevision("v2 nháp", "{}", "h2");
        var shop = BibleEntry.Create(Guid.NewGuid(), BibleEntryCategory.Location, "Tiệm", "Desc");
        shop.AddRevision("v1", "{}", "h3", isInitialVersion: true);
        var term = BibleEntry.Create(Guid.NewGuid(), BibleEntryCategory.Terminology, "Aura", "Desc");
        term.AddRevision("v1", "{}", "h4", isInitialVersion: true);

        hero.GetDefaultVariantRevision(canonOnly: true).Should().BeNull("chưa khoá bản nào");
        v1.Lock(Guid.NewGuid());

        hero.GetDefaultVariantRevision()!.VersionNumber.Should().Be(2, "màn hình vẫn thấy bản nháp mới nhất");
        hero.GetDefaultVariantRevision(canonOnly: true)!.VersionNumber.Should().Be(1, "AI chỉ dùng bản đã khoá");
        shop.GetDefaultVariantRevision(canonOnly: true).Should().BeNull("Địa điểm cũng phải khoá như Nhân vật");
        term.GetDefaultVariantRevision(canonOnly: true).Should().NotBeNull("mục chỉ có văn bản không cần khoá");
    }
}
