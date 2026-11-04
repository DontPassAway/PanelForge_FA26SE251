using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace PanelForge.AI.Gateway;

/// <summary>Pipeline timeout + retry có giới hạn + circuit breaker, dùng chung (singleton) để trạng thái mạch tồn tại qua nhiều run.</summary>
public sealed class LlmResilience
{
    public ResiliencePipeline Pipeline { get; }

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly TimeSpan _minInterval;
    private readonly TimeSpan _cooldownBase, _cooldownMax;
    private DateTime _lastCallUtc = DateTime.MinValue;
    private DateTime _cooldownUntilUtc = DateTime.MinValue;
    private int _rateLimitStreak;

    /// <summary>
    /// Giãn cách THÍCH ỨNG (dùng chung mọi run): bình thường không chờ (trừ MinCallIntervalMs nếu cấu hình), chỉ khi provider đã trả 429
    /// thì mọi lần gọi sau chờ hết thời gian nghỉ (Retry-After hoặc lùi luỹ thừa), rồi trở lại tốc độ bình thường khi gọi thành công.
    /// </summary>
    public async Task ThrottleAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var notBefore = _lastCallUtc + _minInterval;
            if (_cooldownUntilUtc > notBefore) notBefore = _cooldownUntilUtc;
            var wait = notBefore - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _lastCallUtc = DateTime.UtcNow;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Provider trả 429: nghỉ theo Retry-After nếu có, không thì base × 2^(lần liên tiếp - 1), tối đa cooldownMax.</summary>
    public void RegisterRateLimited(TimeSpan? retryAfter)
    {
        lock (_sync)
        {
            _rateLimitStreak++;
            var backoff = retryAfter ?? TimeSpan.FromTicks(_cooldownBase.Ticks * (1L << Math.Min(_rateLimitStreak - 1, 6)));
            if (backoff > _cooldownMax) backoff = _cooldownMax;
            var until = DateTime.UtcNow + backoff;
            if (until > _cooldownUntilUtc) _cooldownUntilUtc = until;
        }
    }

    public void RegisterSuccess()
    {
        lock (_sync) _rateLimitStreak = 0;
    }

    /// <summary>Thời gian nghỉ còn lại (cho test/health).</summary>
    public TimeSpan CooldownRemaining => _cooldownUntilUtc > DateTime.UtcNow ? _cooldownUntilUtc - DateTime.UtcNow : TimeSpan.Zero;

    public LlmResilience(IOptions<ProviderOptions> options)
    {
        var o = options.Value;
        _minInterval = TimeSpan.FromMilliseconds(Math.Max(0, o.MinCallIntervalMs));
        _cooldownBase = TimeSpan.FromSeconds(Math.Max(1, o.RateLimitCooldownSeconds));
        _cooldownMax = TimeSpan.FromSeconds(Math.Max(o.RateLimitCooldownSeconds, o.RateLimitCooldownMaxSeconds));
        var transient = new PredicateBuilder()
            .Handle<HttpRequestException>()
            .Handle<TimeoutRejectedException>()
            .Handle<ClientResultException>(IsTransient);

        Pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = transient,
                MaxRetryAttempts = o.MaxRetries,
                Delay = TimeSpan.FromSeconds(o.RetryBaseDelaySeconds),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = transient,
                FailureRatio = o.BreakerFailureRatio,
                SamplingDuration = TimeSpan.FromSeconds(o.BreakerSamplingSeconds),
                MinimumThroughput = o.BreakerMinimumThroughput,
                BreakDuration = TimeSpan.FromSeconds(o.BreakerBreakSeconds)
            })
            .AddTimeout(TimeSpan.FromSeconds(o.CallTimeoutSeconds))
            .Build();
    }

    internal static bool IsTransient(ClientResultException e) => e.Status is 408 or 429 or >= 500;
}

/// <summary>
/// Cổng duy nhất tới model: mọi agent gọi qua IChatClient này. Thêm timeout/retry/circuit breaker;
/// lỗi tạm thời được thử lại có giới hạn, mạch mở thì trả lỗi ngay (BrokenCircuitException) mà không gọi provider.
/// </summary>
public sealed class LlmGateway : DelegatingChatClient
{
    private readonly ResiliencePipeline _pipeline;
    private readonly LlmResilience _resilience;

    public LlmGateway(IChatClient inner, LlmResilience resilience) : base(inner)
    {
        _pipeline = resilience.Pipeline;
        _resilience = resilience;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages as IList<ChatMessage> ?? messages.ToList();
        return await _pipeline.ExecuteAsync(async ct =>
        {
            // Mỗi lần thử (kể cả retry) đều qua cổng giãn cách: sau 429 mọi run cùng nghỉ, không dồn dập thêm
            await _resilience.ThrottleAsync(ct);
            try
            {
                var response = await base.GetResponseAsync(list, options, ct);
                _resilience.RegisterSuccess();
                return response;
            }
            catch (ClientResultException e) when (e.Status == 429)
            {
                _resilience.RegisterRateLimited(RetryAfter(e));
                throw;
            }
        }, cancellationToken);
    }

    private static TimeSpan? RetryAfter(ClientResultException e)
    {
        var raw = e.GetRawResponse();
        if (raw is null || !raw.Headers.TryGetValue("Retry-After", out var value)) return null;
        return int.TryParse(value, out var seconds) && seconds > 0 ? TimeSpan.FromSeconds(Math.Min(seconds, 120)) : null;
    }

    /// <summary>Mã lỗi ổn định (ghi vào step) + thông điệp không chứa nội dung phản hồi/khoá của provider.</summary>
    public static (string Code, string Message) Classify(Exception ex) => ex switch
    {
        BrokenCircuitException => ("PROVIDER_UNAVAILABLE", "Circuit breaker đang mở do provider lỗi liên tiếp; thử lại sau."),
        TimeoutRejectedException => ("PROVIDER_UNAVAILABLE", "Provider không phản hồi trong thời gian cho phép."),
        HttpRequestException => ("PROVIDER_UNAVAILABLE", "Không kết nối được provider."),
        ClientResultException { Status: 401 or 403 } e => ("PROVIDER_AUTH_FAILED", $"Provider từ chối API key (HTTP {e.Status})."),
        ClientResultException { Status: 404 } => ("MODEL_UNAVAILABLE", "Model không tồn tại hoặc không khả dụng cho API key này (HTTP 404)."),
        ClientResultException { Status: 429 } => ("PROVIDER_UNAVAILABLE", "Provider giới hạn tần suất/quota (HTTP 429) sau khi đã thử lại."),
        ClientResultException e => ("PROVIDER_ERROR", $"Provider trả lỗi HTTP {e.Status}."),
        OperationCanceledException => ("RUN_TIMEOUT", "Run bị huỷ hoặc quá thời gian."),
        _ => ("PROVIDER_ERROR", $"Lỗi khi gọi provider ({ex.GetType().Name}).")
    };
}