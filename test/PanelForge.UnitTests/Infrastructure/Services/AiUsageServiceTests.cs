using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Features.AiUsage;
using PanelForge.Application.Interfaces;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.Infrastructure.Services;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Infrastructure.Services;

/// <summary>UC-02 (hạn mức / tắt AI) và UC-15 (AIUsageRecord + báo cáo).</summary>
public class AiUsageServiceTests
{
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly PanelForgeDbContext _db;
    private readonly AiUsageService _service;

    public AiUsageServiceTests()
    {
        var currentUser = new FakeCurrentUser(_userId, "writer@test.com");
        _db = InMemoryDb.Create(currentUser);
        _service = new AiUsageService(_db, currentUser);
    }

    private async Task<WorkspaceAiConfig> AddConfigAsync(long quota, bool enabled = true, string? apiKey = "encrypted-key")
    {
        var config = WorkspaceAiConfig.Create(_workspaceId, "OpenAI", apiKey, quota);
        config.UpdateSettings("OpenAI", null, enabled, quota, "[\"gpt-4o\"]");
        _db.WorkspaceAiConfigs.Add(config);
        await _db.SaveChangesAsync();
        return config;
    }

    [Fact]
    public async Task Check_WithoutConfig_ShouldBeDisabled()
    {
        var result = await _service.CheckAvailabilityAsync(_workspaceId);

        result.IsAvailable.Should().BeFalse();
        result.BlockedReason.Should().Be(AiUsageStatus.Disabled);
    }

    [Fact]
    public async Task Check_WhenDisabled_ShouldBeDisabled()
    {
        await AddConfigAsync(1000, enabled: false);

        (await _service.CheckAvailabilityAsync(_workspaceId)).BlockedReason.Should().Be(AiUsageStatus.Disabled);
    }

    [Fact]
    public async Task Record_Succeeded_ShouldStoreRecord_AndConsumeQuota_UntilExceeded()
    {
        var config = await AddConfigAsync(quota: 100);

        (await _service.CheckAvailabilityAsync(_workspaceId)).IsAvailable.Should().BeTrue();

        var record = await _service.RecordAsync(new AiUsageEntry(
            _workspaceId, "ConsistencyCheck", AiUsageStatus.Succeeded, "gpt-4o", 60, 40, 0.0012m));

        record.TotalTokens.Should().Be(100);
        record.UserId.Should().Be(_userId);
        record.Provider.Should().Be("OpenAI");
        config.UsedTokensCurrentMonth.Should().Be(100);

        var check = await _service.CheckAvailabilityAsync(_workspaceId);
        check.IsAvailable.Should().BeFalse();
        check.BlockedReason.Should().Be(AiUsageStatus.QuotaExceeded);
    }

    [Fact]
    public async Task Record_Failed_ShouldNotConsumeQuota()
    {
        var config = await AddConfigAsync(quota: 100);

        await _service.RecordAsync(new AiUsageEntry(
            _workspaceId, "ScriptAssist", AiUsageStatus.ProviderUnavailable, "gpt-4o", 50, 0, ErrorMessage: "timeout"));

        config.UsedTokensCurrentMonth.Should().Be(0);
        (await _db.AiUsageRecords.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Check_InNewMonth_ShouldResetUsage()
    {
        var config = await AddConfigAsync(quota: 100);
        config.RecordUsage(100);
        typeof(WorkspaceAiConfig).GetProperty(nameof(WorkspaceAiConfig.LastResetAt))!
            .SetValue(config, DateTime.UtcNow.AddMonths(-1));
        await _db.SaveChangesAsync();

        var check = await _service.CheckAvailabilityAsync(_workspaceId);

        check.IsAvailable.Should().BeTrue();
        config.UsedTokensCurrentMonth.Should().Be(0);
    }

    [Fact]
    public async Task Report_ShouldAggregateRecordsPerWorkspaceAndFeature()
    {
        await AddConfigAsync(quota: 1000);
        await _service.RecordAsync(new AiUsageEntry(_workspaceId, "ConsistencyCheck", AiUsageStatus.Succeeded, "gpt-4o", 100, 50, 0.01m));
        await _service.RecordAsync(new AiUsageEntry(_workspaceId, "ConsistencyCheck", AiUsageStatus.Succeeded, "gpt-4o", 10, 5, 0.001m));
        await _service.RecordAsync(new AiUsageEntry(_workspaceId, "ScriptAssist", AiUsageStatus.Failed, "gpt-4o", 0, 0));
        await _service.RecordAsync(new AiUsageEntry(_workspaceId, "ScriptAssist", AiUsageStatus.QuotaExceeded));

        var result = await new GetAiUsageReportQueryHandler(_db).Handle(new GetAiUsageReportQuery(), default);

        var row = result.Value!.Single();
        row.WorkspaceId.Should().Be(_workspaceId);
        row.CallCount.Should().Be(4);
        row.SucceededCount.Should().Be(2);
        row.FailedCount.Should().Be(1);
        row.BlockedCount.Should().Be(1);
        row.TotalTokens.Should().Be(165);
        row.TotalCostUsd.Should().Be(0.011m);
        row.UsedTokensCurrentMonth.Should().Be(165);
        row.ByFeature.First().Feature.Should().Be("ConsistencyCheck");
    }

    [Fact]
    public async Task Records_ShouldFilterByStatus()
    {
        await AddConfigAsync(quota: 1000);
        await _service.RecordAsync(new AiUsageEntry(_workspaceId, "A", AiUsageStatus.Succeeded, PromptTokens: 1));
        await _service.RecordAsync(new AiUsageEntry(_workspaceId, "B", AiUsageStatus.Failed));

        var result = await new GetAiUsageRecordsQueryHandler(_db).Handle(
            new GetAiUsageRecordsQuery(Status: AiUsageStatus.Failed), default);

        result.Value!.TotalCount.Should().Be(1);
        result.Value.Items.Single().Feature.Should().Be("B");
    }
}
