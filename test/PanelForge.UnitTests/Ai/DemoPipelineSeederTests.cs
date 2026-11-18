using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Infrastructure.Persistence.Seeders;
using Xunit;

namespace PanelForge.UnitTests.Ai;

public sealed class DemoPipelineSeederTests
{
    [Fact]
    public async Task EnsureAsync_AddsPipelineOnce()
    {
        var h = await AiHarness.CreateAsync();

        (await DemoPipelineSeeder.EnsureAsync(h.Db, default, [AiDemoSeeder.SeriesId])).Should().Be(1);
        (await DemoPipelineSeeder.EnsureAsync(h.Db, default, [AiDemoSeeder.SeriesId])).Should().Be(0);

        var series = await h.Db.Series.SingleAsync(s => s.Id == AiDemoSeeder.SeriesId);
        var pipeline = await h.Db.PipelineDefinitions.Include(p => p.Stages).SingleAsync(p => p.SeriesId == series.Id);
        series.PipelineDefinitionId.Should().Be(pipeline.Id);
        pipeline.Stages.Should().Contain(s => s.IsInitial).And.Contain(s => s.IsTerminal);
    }
}
