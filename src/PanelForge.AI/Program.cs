using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PanelForge.AI.Clients;
using PanelForge.AI.Contracts;
using PanelForge.AI.Gateway;
using PanelForge.AI.Workflows;

// Chế độ CLI: chấm Continuity Agent trên bộ 80 mẫu (xem Evaluation/ContinuityEval.cs), không khởi động web host.
if (args.Length > 0 && args[0] == "eval") return await PanelForge.AI.Evaluation.ContinuityEval.RunAsync(args[1..]);

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
builder.Services.Configure<ImageGenOptions>(builder.Configuration.GetSection(ImageGenOptions.SectionName));
builder.Services.AddSingleton<ImageGeneratorFactory>();
// Sinh ảnh: KHÔNG retry (mỗi ảnh tốn quota), timeout dài vì một ảnh mất 10–30 giây
builder.Services.AddHttpClient(ImageGeneratorFactory.HttpClientName, (sp, c) =>
    c.Timeout = TimeSpan.FromSeconds(Math.Clamp(sp.GetRequiredService<IOptions<ImageGenOptions>>().Value.TimeoutSeconds, 10, 180)));
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

// Copilot (sync, một lần gọi model): Backend đã guard quyền/assignment/BR-12/quota; ở đây chỉ kiểm khoá dịch vụ + hợp lệ request.
app.MapPost("/copilot/{task}", async (string task, CopilotRequest req, HttpContext http, ChatClientFactory factory,
    IOptions<ProviderOptions> options, CancellationToken ct) =>
{
    if (!KeyOk(http)) return Results.Json(new AiRunAck(false, "UNAUTHORIZED", "Khoá dịch vụ không hợp lệ."), statusCode: 401);
    if (task != req.Task || !PanelForge.AI.Agents.CopilotAgent.Supports(task) || req.SchemaVersion != AiProtocol.SchemaVersion
        || req.Provider is not (AiProtocol.ProviderMock or AiProtocol.ProviderGemini) || string.IsNullOrWhiteSpace(req.Model)
        || (req.Provider == AiProtocol.ProviderGemini && string.IsNullOrWhiteSpace(req.ApiKey))
        || (CopilotTasks.IsLineTask(task) ? string.IsNullOrWhiteSpace(req.Line?.Dialogue) : req.Series is null)
        || (task == CopilotTasks.LettererShorten && req.MaxChars is not (> 0 and <= CopilotRules.MaxCharsLimit)))
        return Results.Json(new AiRunAck(false, "INVALID_JOB", "Request Copilot không hợp lệ."), statusCode: 400);

    var chat = factory.Create(new AiRunJob(Guid.NewGuid(), req.Provider, req.Model, req.ApiKey, 0));
    var agent = new PanelForge.AI.Agents.CopilotAgent(new PanelForge.AI.Agents.AgentRunner(chat, options));
    return Results.Json(await agent.RunAsync(req, ct), AiJson.Options);
});

// Sinh MỘT ảnh (sync). Backend đã guard quyền/assignment/BR-12/quota ảnh và giới hạn job/Series; ở đây chỉ kiểm khoá + request.
app.MapPost("/images/generate", async (ImageGenRequest req, HttpContext http, ImageGeneratorFactory factory, ILogger<Program> log,
    CancellationToken ct) =>
{
    if (!KeyOk(http)) return Results.Json(new AiRunAck(false, "UNAUTHORIZED", "Khoá dịch vụ không hợp lệ."), statusCode: 401);
    if (req.SchemaVersion != AiProtocol.SchemaVersion || string.IsNullOrWhiteSpace(req.Model)
        || string.IsNullOrWhiteSpace(req.Prompt) || req.Prompt.Length > 4000 || req.References.Count > 3
        || (req.Provider == ImageProviders.Gemini && string.IsNullOrWhiteSpace(req.ApiKey))
        || factory.Create(req.Provider) is not { } generator)
        return Results.Json(new AiRunAck(false, "INVALID_JOB", "Request sinh ảnh không hợp lệ."), statusCode: 400);

    var started = DateTime.UtcNow;
    try
    {
        return Results.Json(await generator.GenerateAsync(req, ct), AiJson.Options);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException or System.Text.Json.JsonException
                                   or InvalidOperationException && !ct.IsCancellationRequested)
    {
        log.LogWarning("Sinh ảnh {Provider}/{Model} lỗi ({Type})", req.Provider, req.Model, ex.GetType().Name);
        return Results.Json(new ImageGenResponse(false, null, null, false, null, null, started, DateTime.UtcNow,
            "AI_UNAVAILABLE", ex is TaskCanceledException
                ? "Nhà cung cấp ảnh không phản hồi trong thời gian cho phép."
                : "Không gọi được nhà cung cấp ảnh."), AiJson.Options);
    }
});

// So ảnh panel với character sheet (sync, một lần gọi vision). Backend đã guard quyền/quota; ở đây chỉ kiểm khoá + request.
app.MapPost("/vision/compare", async (VisionCompareRequest req, HttpContext http, ChatClientFactory factory, CancellationToken ct) =>
{
    if (!KeyOk(http)) return Results.Json(new AiRunAck(false, "UNAUTHORIZED", "Khoá dịch vụ không hợp lệ."), statusCode: 401);
    if (req.SchemaVersion != AiProtocol.SchemaVersion || req.Provider is not (AiProtocol.ProviderMock or AiProtocol.ProviderGemini)
        || string.IsNullOrWhiteSpace(req.Model) || string.IsNullOrWhiteSpace(req.CharacterName)
        || string.IsNullOrEmpty(req.CharacterSheet.Base64) || string.IsNullOrEmpty(req.Artwork.Base64)
        || (req.Provider == AiProtocol.ProviderGemini && string.IsNullOrWhiteSpace(req.ApiKey)))
        return Results.Json(new AiRunAck(false, "INVALID_JOB", "Request vision không hợp lệ."), statusCode: 400);

    var chat = factory.Create(new AiRunJob(Guid.NewGuid(), req.Provider, req.Model, req.ApiKey, 0));
    return Results.Json(await new PanelForge.AI.Agents.VisionAgent(chat).RunAsync(req, ct), AiJson.Options);
});

// Health riêng của AI (tách khỏi health của Backend chính)
app.MapGet("/health/ai", (RunQueue queue) => Results.Ok(new { status = "ok", queueDepth = queue.Depth }));

app.Run();
return 0;