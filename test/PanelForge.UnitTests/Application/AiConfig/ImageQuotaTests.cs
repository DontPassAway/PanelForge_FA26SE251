using FluentAssertions;
using Moq;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiConfig.Commands.UpdateWorkspaceAiConfig;
using PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceUsage;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.AiConfig;

/// <summary>V1.2 (CF1): quota ảnh sinh ra theo Workspace, cùng bộ giới hạn (cấu hình, tháng, reset) với token AI.</summary>
public class ImageQuotaTests
{
    private static WorkspaceAiConfig Config(long imageQuota, bool enabled = true)
    {
        var config = WorkspaceAiConfig.Create(Guid.NewGuid());
        config.UpdateSettings("Google Gemini", null, enabled, 1_000_000, "[\"gemini-2.5-flash\"]");
        config.SetMonthlyImageQuota(imageQuota);
        return config;
    }

    [Fact]
    public void HasImageQuotaFor_ZeroQuotaIsUnlimited_ButDisabledConfigAlwaysBlocks()
    {
        Config(0).HasImageQuotaFor(1_000_000).Should().BeTrue();
        Config(0, enabled: false).HasImageQuotaFor(1).Should().BeFalse();
    }

    [Fact]
    public void HasImageQuotaFor_AllowsUpToQuotaExactly_ThenBlocks()
    {
        var config = Config(10);

        config.HasImageQuotaFor(10).Should().BeTrue();
        config.RecordImages(8);
        config.HasImageQuotaFor(2).Should().BeTrue();
        config.HasImageQuotaFor(3).Should().BeFalse();
        config.UsedImagesCurrentMonth.Should().Be(8);
    }

    [Fact]
    public void RecordImages_IgnoresNonPositiveCounts_AndNegativeQuotaBecomesUnlimited()
    {
        var config = Config(10);

        config.RecordImages(0);
        config.RecordImages(-5);
        config.UsedImagesCurrentMonth.Should().Be(0);

        config.SetMonthlyImageQuota(-3);
        config.MonthlyImageQuota.Should().Be(0);
    }

    [Fact]
    public void ResetMonthlyUsage_ClearsImagesTogetherWithTokens()
    {
        var config = Config(10);
        config.RecordUsage(500);
        config.RecordImages(4);

        config.ResetMonthlyUsage();

        config.UsedTokensCurrentMonth.Should().Be(0);
        config.UsedImagesCurrentMonth.Should().Be(0);
        config.MonthlyImageQuota.Should().Be(10, "quota là cấu hình, chỉ mức đã dùng bị reset");
    }

    [Fact]
    public async Task AdminUpdate_SetsImageQuota_AndNullKeepsTheCurrentOne()
    {
        var workspace = StudioWorkspace.Create("Studio", Guid.NewGuid());
        var config = WorkspaceAiConfig.Create(workspace.Id);
        var db = new Mock<IPanelForgeDbContext>();
        db.Setup(d => d.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace }));
        db.Setup(d => d.WorkspaceAiConfigs).Returns(DbSetMockHelper.CreateDbSetMock(new List<WorkspaceAiConfig> { config }));
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var handler = new UpdateWorkspaceAiConfigCommandHandler(db.Object, Mock.Of<IAiKeyProtector>(), Mock.Of<IAiProviderValidator>());
        UpdateWorkspaceAiConfigCommand Cmd(long? images) =>
            new(workspace.Id, "Google Gemini", null, true, 1_000_000, ["gemini-2.5-flash"], images);

        var set = await handler.Handle(Cmd(200), CancellationToken.None);
        var kept = await handler.Handle(Cmd(null), CancellationToken.None);

        set.Value!.MonthlyImageQuota.Should().Be(200);
        kept.Value!.MonthlyImageQuota.Should().Be(200);
    }

    [Fact]
    public async Task Usage_ReportsImagesAgainstQuota_AndNullWithoutAiConfig()
    {
        var id = Guid.NewGuid();
        var workspace = StudioWorkspace.Create("Studio", Guid.NewGuid());
        typeof(StudioWorkspace).GetProperty("Id")!.SetValue(workspace, id);
        var config = WorkspaceAiConfig.Create(id);
        config.SetMonthlyImageQuota(40);
        config.RecordImages(10);

        GetWorkspaceUsageQueryHandler Handler(List<WorkspaceAiConfig> configs)
        {
            var db = new Mock<IPanelForgeDbContext>();
            db.Setup(d => d.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace }));
            db.Setup(d => d.WorkspaceAiConfigs).Returns(DbSetMockHelper.CreateDbSetMock(configs));
            return new GetWorkspaceUsageQueryHandler(db.Object);
        }

        var withConfig = await Handler([config]).Handle(new GetWorkspaceUsageQuery(id), CancellationToken.None);
        var withoutConfig = await Handler([]).Handle(new GetWorkspaceUsageQuery(id), CancellationToken.None);

        withConfig.Value!.Images.Should().Be(new UsageMeterDto(10, 40, false, 25.0));
        withoutConfig.Value!.Images.Should().BeNull();
    }
}
