using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PanelForge.AI.Clients;
using PanelForge.AI.Contracts;
using PanelForge.AI.Gateway;
using PanelForge.AI.Workflows;

// PanelForge.AI — service chạy workflow agent. KHÔNG truy cập PostgreSQL; chỉ nghe localhost (xem appsettings.json → Kestrel).
var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ProviderOptions>(builder.Configuration.GetSection(ProviderOptions.SectionName));
builder.Services.Configure<WorkflowOptions>(builder.Configuration.GetSection(WorkflowOptions.SectionName));
builder.Services.Configure<BackendOptions>(builder.Configuration.GetSection(BackendOptions.SectionName));

builder.Services.AddSingleton<LlmResilience>();
builder.Services.AddSingleton<RunQueue>();
builder.Services.AddSingleton<ChatClientFactory>();
builder.Services.AddScoped<MultiAgentWorkflow>();
builder.Services.AddHostedService<RunWorker>();

builder.Services.AddHttpClient("gemini-probe", c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<BackendClient>((sp, client) =>
    {
        var backend = sp.GetRequiredService<IOptions<BackendOptions>>().Value;
        client.BaseAddress = new Uri(backend.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Add(AiProtocol.ServiceKeyHeader, sp.GetRequiredService<IConfiguration>()["AiService:ServiceKey"] ?? "");
    })
    .AddStandardResilienceHandler(o =>
    {
        // Callback step idempotent theo (runId, stepIndex) → retry an toàn
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(15);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(40);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        o.Retry.MaxRetryAttempts = 2;
    });

var app = builder.Build();

if (string.IsNullOrEmpty(app.Configuration["AiService:ServiceKey"]))
    app.Logger.LogWarning("Chưa cấu hình AiService:ServiceKey (user-secrets): mọi request giao job sẽ bị từ chối.");

bool KeyOk(HttpContext http)
{
    var expected = app.Configuration["AiService:ServiceKey"];
    var provided = http.Request.Headers[AiProtocol.ServiceKeyHeader].ToString();
    return !string.IsNullOrEmpty(expected)
           && CryptographicOperations.FixedTimeEquals(
               SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(provided)));
}

// Backend giao một run. Trả 202 khi đã vào hàng đợi (idempotent theo runId); 429 khi hàng đợi đầy.
app.MapPost("/runs", (AiRunJob job, HttpContext http, RunQueue queue) =>
{
    if (!KeyOk(http)) return Results.Json(new AiRunAck(false, "UNAUTHORIZED", "Khoá dịch vụ không hợp lệ."), statusCode: 401);

    if (job.RunId == Guid.Empty || job.SchemaVersion != AiProtocol.SchemaVersion
        || job.Provider is not (AiProtocol.ProviderMock or AiProtocol.ProviderGemini)
        || string.IsNullOrWhiteSpace(job.Model)
        || job.MaxRevisionRounds is < 0 or > 5
        || (job.Provider == AiProtocol.ProviderGemini && string.IsNullOrWhiteSpace(job.ApiKey)))
        return Results.Json(new AiRunAck(false, "INVALID_JOB", "Job không hợp lệ."), statusCode: 400);

    return queue.TryEnqueue(job) switch
    {
        EnqueueResult.Full => Results.Json(new AiRunAck(false, "QUEUE_FULL", "Hàng đợi đầy."), statusCode: 429),
        _ => Results.Json(new AiRunAck(true), statusCode: 202)
    };
});

// Health riêng của AI (tách khỏi health của Backend chính)
app.MapGet("/health/ai", (RunQueue queue) => Results.Ok(new { status = "ok", queueDepth = queue.Depth }));

app.Run();