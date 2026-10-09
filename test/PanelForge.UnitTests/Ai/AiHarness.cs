using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PanelForge.AI.Clients;
using PanelForge.AI.Contracts;
using PanelForge.AI.Demo;
using PanelForge.AI.Gateway;
using PanelForge.AI.Workflows;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Commands.RecordAgentStep;
using PanelForge.Application.Features.AiAgents.Commands.ReviewSuggestion;
using PanelForge.Application.Features.AiAgents.Commands.RunWorkflow;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Application.Features.AiAgents.Queries.GetAgentContext;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.Infrastructure.Persistence.Seeders;
using PanelForge.Infrastructure.Services;
using PanelForge.UnitTests.Common;

namespace PanelForge.UnitTests.Ai;

/// <summary>
/// Dựng dữ liệu demo thật (DevUserSeeder + AiDemoSeeder) trên EF InMemory và nối hai phía của hợp đồng HTTP trong tiến trình:
/// MultiAgentWorkflow (PanelForge.AI) gọi BackendClient → InProcessBackendHandler → handler thật của Backend. Không có mạng.
/// </summary>
public sealed class AiHarness
{
    public static readonly Guid Producer = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Writer = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid Editor = Guid.Parse("77777777-7777-7777-7777-777777777777");
    public static readonly Guid Artist = Guid.Parse("55555555-5555-5555-5555-555555555555");
    public static readonly Guid Reader = Guid.Parse("88888888-8888-8888-8888-888888888888");
    public static readonly Guid Letterer = Guid.Parse("66666666-6666-6666-6666-666666666666");

    public PanelForgeDbContext Db { get; }
    public AiAgentSettings Settings { get; } = new() { Provider = "Mock", RunStaleAfterSeconds = 300 };
    public Mock<IAiClient> AiClient { get; } = new();
    public Mock<IAiUsageService> Usage { get; } = new();
    public Mock<IAiKeyProtector> KeyProtector { get; } = new();
    public Mock<IPageRepository> Pages { get; } = new();
    public IWorkspaceAuthorizationService Auth { get; }
    public AiRunJob? LastJob { get; private set; }
    public Dictionary<Guid, PanelForge.Domain.Entities.Content.Page> SavedPages { get; } = [];

    private AiHarness(PanelForgeDbContext db)
    {
        Db = db;
        Auth = new WorkspaceAuthorizationService(db);
        AiClient.Setup(c => c.StartRunAsync(It.IsAny<AiRunJob>(), It.IsAny<CancellationToken>()))
            .Callback<AiRunJob, CancellationToken>((j, _) => LastJob = j)
            .ReturnsAsync(new AiRunAck(true));
        Usage.Setup(u => u.RecordAsync(It.IsAny<AiUsageEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiUsageEntry e, CancellationToken _) =>
                AiUsageRecord.Create(e.WorkspaceId, e.Feature, "Google Gemini", e.Status));
        Usage.Setup(u => u.CheckAvailabilityAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AiAvailability.Available());
        // Marten giả: nhớ các Page đã lưu (do Accept layout) để test có thể thêm bubble/element rồi trả lại qua GetAsync
        Pages.Setup(p => p.SaveAsync(It.IsAny<PanelForge.Domain.Entities.Content.Page>(), It.IsAny<CancellationToken>()))
            .Callback<PanelForge.Domain.Entities.Content.Page, CancellationToken>((page, _) => SavedPages[page.Id] = page)
            .Returns(Task.CompletedTask);
        Pages.Setup(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => SavedPages.GetValueOrDefault(id));
    }

    public static async Task<AiHarness> CreateAsync()
    {
        var db = InMemoryDb.Create(new FakeCurrentUser(Producer, "producer@panelforge.com"));
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("hash");
        await DevUserSeeder.SeedAsync(db, hasher.Object);
        (await AiDemoSeeder.SeedAsync(db)).Should().BeTrue();
        // Seeder demo thêm sẵn Letterer (tài khoản dev letterer@) nên harness không cần tự thêm
        return new AiHarness(db);
    }

    public RunWorkflowCommandHandler RunHandler() =>
        new(Db, Auth, Usage.Object, KeyProtector.Object, AiClient.Object, Settings, Pages.Object);

    /// <summary>Người dùng mặc định có quyền chạy từng loại workflow (theo chính sách của Backend).</summary>
    public static Guid DefaultUser(AiWorkflowType type) => type switch
    {
        AiWorkflowType.Artist => Artist,
        AiWorkflowType.Letterer => Letterer,
        AiWorkflowType.Editor => Editor,
        AiWorkflowType.Producer => Producer,
        _ => Writer
    };

    /// <summary>Lệnh chạy với version nguồn HIỆN TẠI (chapter version + script revision mới nhất) — client "tải lại rồi chạy".</summary>
    public async Task<RunWorkflowCommand> CommandAsync(
        AiWorkflowType type, Guid? user = null, string brief = "Nhiệm vụ kiểm thử.")
    {
        var chapter = await Db.Chapters.AsNoTracking().SingleAsync();
        var latestScript = await Db.ContentRevisions.AsNoTracking()
            .Where(r => r.SceneId == AiDemoSeeder.SceneId && r.Kind == ContentRevisionKind.Script)
            .OrderByDescending(r => r.VersionNumber).Select(r => (Guid?)r.Id).FirstOrDefaultAsync();
        return new RunWorkflowCommand(
            AiDemoSeeder.SeriesId, user ?? DefaultUser(type), AiDemoSeeder.ChapterId,
            type == AiWorkflowType.Producer ? null : AiDemoSeeder.SceneId, brief, ReadingDirection.RightToLeft,
            chapter.VersionVector, latestScript, type);
    }

    public async Task<Guid> StartAsync(AiWorkflowType type, Guid? user = null, string? brief = null)
    {
        var cmd = await CommandAsync(type, user);
        if (brief is not null) cmd = cmd with { Brief = brief };
        var result = await RunHandler().Handle(cmd, default);
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        return result.Value!.RunId;
    }

    /// <summary>Chạy workflow Mock trọn vẹn qua hai phía hợp đồng (AI service thật + handler Backend thật).</summary>
    public async Task<Guid> RunMockAsync(AiWorkflowType type, Guid? user = null, string? brief = null, IChatClient? chat = null)
    {
        var runId = await StartAsync(type, user, brief);
        await ExecuteWorkflowAsync(runId, chat);
        return runId;
    }

    /// <summary>Tạo dữ liệu nền cho Artist/Letterer/Editor: chạy FullScene (Mock) rồi Accept script + layout đúng version.</summary>
    public async Task AcceptScriptAndLayoutAsync()
    {
        var runId = await RunMockAsync(AiWorkflowType.FullScene);
        var script = await SuggestionAsync(runId, AiSuggestionKind.Script);
        var layout = await SuggestionAsync(runId, AiSuggestionKind.Layout);
        (await ReviewAsync(script.Id, AiSuggestionStatus.Accepted, Writer, chapterVersion: 0)).IsSuccess.Should().BeTrue();
        (await ReviewAsync(layout.Id, AiSuggestionStatus.Accepted, Writer, chapterVersion: 1)).IsSuccess.Should().BeTrue();
    }

    public ReviewSuggestionCommandHandler ReviewHandler() =>
        new(Db, Auth, new PassThroughUnitOfWork(), Pages.Object);

    public RecordAgentStepCommandHandler RecordHandler() => new(Db, Usage.Object);

    public RunWorkflowCommand RunCommand(
        string brief = "Viết cảnh An dọn tiệm đồng hồ lúc nửa đêm thì bà Lan xuất hiện ngoài hiên.",
        Guid? user = null, long chapterVersion = 0, Guid? baseScriptRevision = null) =>
        new(AiDemoSeeder.SeriesId, user ?? Writer, AiDemoSeeder.ChapterId, AiDemoSeeder.SceneId, brief,
            ReadingDirection.RightToLeft, chapterVersion, baseScriptRevision);

    public async Task<Guid> StartRunAsync(string? brief = null)
    {
        var result = await RunHandler().Handle(brief is null ? RunCommand() : RunCommand(brief), default);
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        return result.Value!.RunId;
    }

    /// <summary>Chạy workflow của PanelForge.AI với Backend thật (in-process) cho run đã tạo.</summary>
    public async Task ExecuteWorkflowAsync(Guid runId, IChatClient? chat = null)
    {
        var http = new HttpClient(new InProcessBackendHandler(this)) { BaseAddress = new Uri("http://backend/") };
        var options = Options.Create(new ProviderOptions());
        var factory = new ChatClientFactory(options, new LlmResilience(options), Mock.Of<IHttpClientFactory>());
        var workflow = new MultiAgentWorkflow(new BackendClient(http), factory, options, NullLogger<MultiAgentWorkflow>.Instance);
        var job = LastJob ?? new AiRunJob(runId, "Mock", "mock-fixture-v1", null, 2);
        await workflow.RunAsync(job, chat ?? new MockChatClient(), default);
    }

    /// <summary>Chạy trọn workflow Mock và trả run đã Completed (script + layout đang Pending).</summary>
    public async Task<Guid> CompleteDemoRunAsync()
    {
        var runId = await StartRunAsync();
        await ExecuteWorkflowAsync(runId);
        return runId;
    }

    public async Task<List<AiAgentStep>> StepsAsync(Guid runId) =>
        await Db.AiAgentSteps.Where(s => s.RunId == runId).OrderBy(s => s.StepIndex).ToListAsync();

    public async Task<AiWorkflowRun> RunAsync(Guid runId) => await Db.AiWorkflowRuns.SingleAsync(r => r.Id == runId);

    public Task<AiSuggestion> SuggestionAsync(Guid runId, AiSuggestionKind kind) =>
        Db.AiSuggestions.SingleAsync(s => s.RunId == runId && s.Kind == kind);

    public Task<Result<ReviewSuggestionResponse>> ReviewAsync(
        Guid suggestionId, AiSuggestionStatus decision, Guid? user = null, long? chapterVersion = 0, string? reason = null) =>
        ReviewHandler().Handle(new ReviewSuggestionCommand(
            AiDemoSeeder.SeriesId, suggestionId, user ?? Writer, decision, reason, chapterVersion), default);

    /// <summary>Đóng vai Backend cho AI service: định tuyến HTTP tới đúng handler (cùng DbContext).</summary>
    private sealed class InProcessBackendHandler(AiHarness h) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');   // internal/ai/runs/{id}/{context|steps}
            var runId = Guid.Parse(segments[3]);

            if (segments[4] == "context")
                return Respond(await new GetAgentContextQueryHandler(h.Db).Handle(new GetAgentContextQuery(runId), ct));

            var report = await request.Content!.ReadFromJsonAsync<AgentStepReport>(AiJson.Options, ct);
            return Respond(await h.RecordHandler().Handle(new RecordAgentStepCommand(runId, report!), ct));
        }

        private static HttpResponseMessage Respond<T>(Result<T> r)
        {
            if (r.IsSuccess)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(r.Value, options: AiJson.Options) };
            var status = r.ErrorCode switch
            {
                ResultErrorCodes.Conflict => HttpStatusCode.Conflict,
                ResultErrorCodes.NotFound => HttpStatusCode.NotFound,
                _ => HttpStatusCode.BadRequest
            };
            return new HttpResponseMessage(status) { Content = new StringContent(r.ErrorMessage ?? "") };
        }
    }
}

/// <summary>IChatClient giả do test điều khiển: trả text theo lần gọi (đếm từ 0) và nội dung tin nhắn người dùng.</summary>
public sealed class ScriptedChatClient(Func<int, string, string> responder) : IChatClient
{
    public int Calls { get; private set; }
    public List<string> UserMessages { get; } = [];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var user = messages.Last(m => m.Role == ChatRole.User).Text;
        UserMessages.Add(user);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responder(Calls++, user))));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}