using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Options;
using PanelForge.AI.Gateway;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>Giãn cách thích ứng: không chờ khi chưa bị 429; sau 429 nghỉ theo Retry-After/lùi luỹ thừa, có trần.</summary>
public sealed class AdaptiveThrottleTests
{
    private static LlmResilience Create() => new(Options.Create(new ProviderOptions
    {
        MinCallIntervalMs = 0, RateLimitCooldownSeconds = 8, RateLimitCooldownMaxSeconds = 20
    }));

    [Fact]
    public async Task NoRateLimit_DoesNotWait()
    {
        var r = Create();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++) await r.ThrottleAsync(default);
        sw.ElapsedMilliseconds.Should().BeLessThan(500);
        r.CooldownRemaining.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void RateLimited_BacksOffExponentially_CappedAndHonoursRetryAfter()
    {
        var r = Create();
        r.RegisterRateLimited(null);
        r.CooldownRemaining.Should().BeCloseTo(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(1));
        r.RegisterRateLimited(null);
        r.CooldownRemaining.Should().BeCloseTo(TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(1));
        r.RegisterRateLimited(null);
        r.CooldownRemaining.Should().BeCloseTo(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(1), "có trần Max");

        var fresh = Create();
        fresh.RegisterRateLimited(TimeSpan.FromSeconds(3));
        fresh.CooldownRemaining.Should().BeCloseTo(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), "ưu tiên Retry-After của provider");
    }
}
