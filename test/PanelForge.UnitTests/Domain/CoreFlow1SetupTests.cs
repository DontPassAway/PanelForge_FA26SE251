using FluentAssertions;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Domain;

public class CoreFlow1SetupTests
{
    [Fact]
    public void UC02_WorkspaceAiConfig_Create_ShouldInitializeWithDefaultQuotas()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();

        // Act
        var config = WorkspaceAiConfig.Create(workspaceId, "Google Gemini", "test-api-key", 500_000);

        // Assert
        config.WorkspaceId.Should().Be(workspaceId);
        config.Provider.Should().Be("Google Gemini");
        config.MonthlyTokenQuota.Should().Be(500_000);
        config.UsedTokensCurrentMonth.Should().Be(0);
        config.IsEnabled.Should().BeTrue();
        config.HasQuotaRemaining().Should().BeTrue();
    }

    [Fact]
    public void UC02_WorkspaceAiConfig_ExhaustQuota_ShouldTriggerCircuitBreaker_NFR03()
    {
        // Arrange
        var config = WorkspaceAiConfig.Create(Guid.NewGuid(), "Google Gemini", "api-key", 100_000);

        // Act
        config.RecordUsage(100_000);

        // Assert - Hết quota thì HasQuotaRemaining trả về false (NFR-03, A-01)
        config.UsedTokensCurrentMonth.Should().Be(100_000);
        config.HasQuotaRemaining().Should().BeFalse();
    }

    [Fact]
    public void UC03_Series_CreateWithGenreFormatAndSchedule_ShouldSetProperties()
    {
        // Arrange
        var wsId = Guid.NewGuid();

        // Act
        var series = Series.Create(
            workspaceId: wsId,
            title: "Cyberpunk Tokyo",
            readingDirection: ReadingDirection.RightToLeft,
            synopsis: "Neo Tokyo in 2088",
            genre: "Sci-Fi / Action",
            format: "Manga",
            releaseScheduleJson: "Hàng tuần vào Thứ 6"
        );

        // Assert
        series.WorkspaceId.Should().Be(wsId);
        series.Title.Should().Be("Cyberpunk Tokyo");
        series.Genre.Should().Be("Sci-Fi / Action");
        series.Format.Should().Be("Manga");
        series.ReleaseScheduleJson.Should().Be("Hàng tuần vào Thứ 6");
        series.ReadingDirection.Should().Be(ReadingDirection.RightToLeft);
    }

    [Fact]
    public void BR15_BibleEntryRevision_ChapterVersioned_ShouldStoreEffectiveFromChapter()
    {
        // Arrange
        var bible = SeriesBible.Create(Guid.NewGuid(), Guid.NewGuid(), "Main Bible");
        var entry = bible.AddEntry(BibleEntryCategory.Character, "CHR-01", "Kira", "Cyber blade wielder");

        // Act 1: Initial Version effective from Chapter 1 (BR-18)
        var rev1 = entry.AddRevision(
            summary: "Initial creation",
            snapshotJson: "{\"arm\":\"flesh\"}",
            contentHash: "hash-v1",
            authorId: Guid.NewGuid(),
            effectiveFromChapterNumber: 1,
            isInitialVersion: true
        );

        // Act 2: Revision effective from Chapter 5 (BR-15 - Fact legitimately changes later in the series)
        var rev2 = entry.AddRevision(
            summary: "Upgraded cybernetic arm in chapter 5 battle",
            snapshotJson: "{\"arm\":\"cybernetic titanium\"}",
            contentHash: "hash-v2",
            authorId: Guid.NewGuid(),
            effectiveFromChapterNumber: 5,
            isInitialVersion: false
        );

        // Assert
        entry.Revisions.Should().HaveCount(2);
        rev1.VersionNumber.Should().Be(1);
        rev1.EffectiveFromChapterNumber.Should().Be(1);
        rev1.IsInitialVersion.Should().BeTrue();

        rev2.VersionNumber.Should().Be(2);
        rev2.EffectiveFromChapterNumber.Should().Be(5);
        rev2.IsInitialVersion.Should().BeFalse();

        // Kiểm tra logic lọc theo chương: Tại Chương 3, version có hiệu lực là rev1 (v1); Tại Chương 5, version có hiệu lực là rev2 (v2)
        var effectiveAtChapter3 = entry.Revisions
            .Where(r => r.EffectiveFromChapterNumber <= 3)
            .OrderByDescending(r => r.EffectiveFromChapterNumber)
            .ThenByDescending(r => r.VersionNumber)
            .First();
        effectiveAtChapter3.VersionNumber.Should().Be(1);

        var effectiveAtChapter6 = entry.Revisions
            .Where(r => r.EffectiveFromChapterNumber <= 6)
            .OrderByDescending(r => r.EffectiveFromChapterNumber)
            .ThenByDescending(r => r.VersionNumber)
            .First();
        effectiveAtChapter6.VersionNumber.Should().Be(2);
    }

    [Fact]
    public void NFR08_SeriesPreset_DefaultTypographyAndRules_ShouldBeInitialized()
    {
        // Arrange
        var seriesId = Guid.NewGuid();

        // Act
        var preset = SeriesPreset.Create(seriesId);

        // Assert
        preset.SeriesId.Should().Be(seriesId);
        preset.TypographyPresetsJson.Should().Contain("dialogue");
        preset.TypographyPresetsJson.Should().Contain("Anime Ace 3");
        preset.ConsistencyRulesJson.Should().Contain("characterContinuity");
        preset.ConsistencyRulesJson.Should().Contain("warnDialogueOverflow");
    }

    [Fact]
    public void BR18_AuditLog_Create_ShouldStoreActorAndChanges()
    {
        // Arrange
        var wsId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Act
        var log = AuditLog.Create(
            action: "CREATE_SERIES",
            entityName: "Series",
            entityId: Guid.NewGuid().ToString(),
            workspaceId: wsId,
            userId: userId,
            userEmail: "producer@panelforge.com",
            details: "Tạo dự án truyện mới trong Workspace"
        );

        // Assert
        log.Action.Should().Be("CREATE_SERIES");
        log.EntityName.Should().Be("Series");
        log.UserEmail.Should().Be("producer@panelforge.com");
        log.WorkspaceId.Should().Be(wsId);
        log.TimestampUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}
