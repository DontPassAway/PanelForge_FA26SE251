using Microsoft.Extensions.AI;
using PanelForge.AI.Agents;
using PanelForge.AI.Clients;
using PanelForge.AI.Contracts;
using PanelForge.AI.Gateway;

namespace PanelForge.AI.Workflows;

/// <summary>
/// Gemini làm đầu não: <see cref="SupervisorAgent"/> điều phối 7 agent chuyên môn (Writer, Continuity, Storyboard, Artist, Letterer, Editor, Producer)
/// theo LOẠI workflow của run (<see cref="AgentContext.WorkflowType"/>) — không workflow nào chạy đủ cả 7.
/// Mỗi bước Supervisor chọn hành động kế tiếp (kèm lý do + chỉ đạo) trong danh sách do CODE tính
/// (<see cref="WorkflowRules"/>: chuỗi hợp lệ + điều kiện Finish riêng từng workflow, tối đa MaxRevisionRounds lượt sửa Writer, script luôn qua Continuity...).
/// Code vẫn kiểm output từng agent, Backend vẫn kiểm lại và chỉ nhận step đúng hàng rào. Mỗi step (kể cả quyết định của Supervisor)
/// được Backend LƯU xong (ack) rồi mới chạy bước kế; output bước trước là input bước sau. Mọi kết quả chỉ là đề xuất/báo cáo.
/// </summary>
public sealed class MultiAgentWorkflow
{
    private static readonly TimeSpan ReportTimeout = TimeSpan.FromSeconds(20);
    // Chặn vòng lặp vô hạn nếu có lỗi logic: workflow hợp lệ dài tối đa ~ (2 + 2*3 + 3) quyết định
    private const int MaxDecisions = 24;

    private readonly BackendClient _backend;
    private readonly ChatClientFactory _factory;
    private readonly Microsoft.Extensions.Options.IOptions<ProviderOptions> _options;
    private readonly ILogger<MultiAgentWorkflow> _logger;

    public MultiAgentWorkflow(
        BackendClient backend, ChatClientFactory factory,
        Microsoft.Extensions.Options.IOptions<ProviderOptions> options, ILogger<MultiAgentWorkflow> logger)
    {
        _backend = backend;
        _factory = factory;
        _options = options;
        _logger = logger;
    }

    public Task RunAsync(AiRunJob job, CancellationToken ct) => RunAsync(job, _factory.Create(job), ct);

    /// <summary>Tách chat client để test truyền IChatClient giả.</summary>
    public async Task RunAsync(AiRunJob job, IChatClient chat, CancellationToken ct)
    {
        var ctx = await _backend.GetContextAsync(job.RunId, ct);
        var keys = new KeyMaps(ctx);
        var runner = new AgentRunner(chat, _options);
        var steps = new StepWriter(_backend, job, _logger);

        // Gemini: kiểm tra model còn khả dụng trước khi tốn lượt gọi; lỗi → step thất bại rõ ràng, KHÔNG fallback sang Mock
        if (job.Provider == AiProtocol.ProviderGemini)
        {
            var probe = await _factory.ProbeGeminiModelAsync(job, ct);
            if (probe is { } p)
            {
                await steps.ReportFailureAsync(AiProtocol.AgentSupervisor, 0, null, p.Code, p.Message, SupervisorAgent.Prompt, ct);
                return;
            }
        }

        var supervisor = new SupervisorAgent(runner);
        var writer = new WriterAgent(runner);
        var continuity = new ContinuityAgent(runner);
        var storyboard = new StoryboardAgent(runner);
        var editor = new EditorAgent(runner);
        var artist = new ArtistAgent(runner);
        var letterer = new LettererAgent(runner);
        var producer = new ProducerAgent(runner);

        var inputs = ctx.Inputs;
        var facts = new List<StepFact>();
        var history = new List<string>();
        // Workflow không có Writer (Storyboard/Editor) dùng script có sẵn của Scene làm bản nháp lượt 0
        var hasWriter = ctx.WorkflowType is AiProtocol.WorkflowFullScene or AiProtocol.WorkflowScript;
        ScriptDraft? draft = hasWriter || !inputs.HasScript ? null : PromptText.BaselineDraft(ctx);
        ContinuityReport? report = null;
        StoryboardLayout? layout = null;
        int? continuityStep = null, writerStep = null, storyboardStep = null;

        for (var turn = 0; turn < MaxDecisions; turn++)
        {
            var allowed = WorkflowRules.AllowedActions(ctx.WorkflowType, inputs, facts, ctx.MaxRevisionRounds);
            if (allowed.Count == 0) return;
            var round = draft?.Round ?? 0;
            var blocking = facts.Count > 0 && facts[^1].HasBlocking
                ? report!.Findings.Count(f => f.Severity == AiProtocol.SeverityBlocking) : 0;

            // ── Supervisor (Gemini) chọn bước kế tiếp trong phạm vi hàng rào ──
            runner.Instruction = null;
            AgentCall<SupervisorDecision> dCall;
            string dInput;
            if (facts.Count == 0 && allowed is [WorkflowRules.ActionStopForHuman])
            {
                // Thiếu context ngay từ đầu: code lập quyết định StopForHuman với lý do rõ ràng (không cần hỏi model)
                dCall = SupervisorAgent.MissingContext(ctx, allowed);
                dInput = AiJson.Serialize(new { round, allowedActions = allowed, history, missingContext = true });
            }
            else if (allowed.Count == 1)
            {
                // Chỉ một bước hợp lệ: không có gì để chọn → code quyết định, tiết kiệm một lần gọi model (quota)
                dCall = SupervisorAgent.SingleChoice(ctx, allowed, round, blocking);
                dInput = AiJson.Serialize(new { round, blockingFindings = blocking, allowedActions = allowed, history });
            }
            else
            {
                (dCall, dInput) = await supervisor.DecideAsync(ctx, history, allowed, round, blocking, ct);
                if (!dCall.Succeeded && dCall.ErrorCode == "INVALID_OUTPUT")
                {
                    // Lựa chọn sai ngay cả sau lần sửa: code chọn hành động an toàn đầu tiên và ghi rõ là bị ghi đè
                    var forced = SupervisorAgent.Override(ctx, allowed, round, dCall.ErrorMessage ?? "output không hợp lệ");
                    dCall = dCall with
                    {
                        Succeeded = true, Payload = forced, OutputJson = AiJson.Serialize(forced), ErrorCode = null, ErrorMessage = null
                    };
                }
            }
            var previousIndex = steps.LastIndex == 0 ? (int?)null : steps.LastIndex;
            var dAck = await steps.ReportAsync(AiProtocol.AgentSupervisor, round, previousIndex, dCall, dInput, SupervisorAgent.Prompt, ct);
            if (dAck is null || !dCall.Succeeded) return;   // kể cả quyết định kết thúc (StopForHuman / Finish): Backend đã đóng run

            var decision = dCall.Payload!;
            runner.Instruction = decision.Instruction;

            // ── Agent chuyên môn được chọn chạy; output của bước trước là input của bước này ──
            switch (decision.Action)
            {
                case AiProtocol.AgentWriter:
                {
                    var wRound = facts.Count > 0 && facts[^1].HasBlocking && draft is not null ? draft.Round + 1 : 0;
                    var (call, input) = await writer.RunAsync(
                        ctx, keys, wRound, wRound > 0 ? draft : null, wRound > 0 ? report : null, ct);
                    var ack = await steps.ReportAsync(AiProtocol.AgentWriter, wRound, wRound > 0 ? continuityStep : null, call, input, WriterAgent.Prompt, ct);
                    if (ack is null || !call.Succeeded) return;
                    draft = call.Payload!;
                    writerStep = steps.LastIndex;
                    facts.Add(new StepFact(AiProtocol.AgentWriter, wRound, true, false));
                    history.Add($"#{writerStep} Writer (lượt {wRound}): {draft.Lines.Count} dòng script.");
                    break;
                }
                case AiProtocol.AgentContinuity:
                {
                    var (call, input) = await continuity.RunAsync(ctx, keys, draft!, ct);
                    var ack = await steps.ReportAsync(AiProtocol.AgentContinuity, draft!.Round, writerStep, call, input, ContinuityAgent.Prompt, ct);
                    if (ack is null || !call.Succeeded) return;
                    report = call.Payload!;
                    continuityStep = steps.LastIndex;
                    facts.Add(new StepFact(AiProtocol.AgentContinuity, draft.Round, true, report.HasBlockingFindings));
                    var nBlocking = report.Findings.Count(f => f.Severity == AiProtocol.SeverityBlocking);
                    history.Add($"#{continuityStep} Continuity (bản lượt {draft.Round}): {report.Findings.Count} finding, {nBlocking} Blocking. {report.Summary}");
                    break;
                }
                case AiProtocol.AgentStoryboard:
                {
                    var (call, input) = await storyboard.RunAsync(ctx, keys, draft!, ct);
                    var ack = await steps.ReportAsync(AiProtocol.AgentStoryboard, 0, continuityStep, call, input, StoryboardAgent.Prompt, ct);
                    if (ack is null || !call.Succeeded) return;
                    layout = call.Payload!;
                    storyboardStep = steps.LastIndex;
                    facts.Add(new StepFact(AiProtocol.AgentStoryboard, 0, true, false));
                    history.Add($"#{storyboardStep} Storyboard: {layout.Pages.Count} trang, {layout.Pages.Sum(p => p.Panels.Count)} panel.");
                    break;
                }
                case AiProtocol.AgentEditor:
                {
                    // Layout: bản Storyboard vừa tạo trong run; workflow Editor dùng layout đã duyệt (nếu có)
                    var (call, input) = await editor.RunAsync(ctx, keys, draft!, report!, layout ?? ctx.ExistingLayout, ct);
                    var ack = await steps.ReportAsync(AiProtocol.AgentEditor, 0, storyboardStep ?? continuityStep, call, input, EditorAgent.Prompt, ct);
                    if (ack is null || !call.Succeeded) return;
                    facts.Add(new StepFact(AiProtocol.AgentEditor, 0, true, false));
                    history.Add($"#{steps.LastIndex} Editor: khuyến nghị {call.Payload!.Verdict}.");
                    break;
                }
                case AiProtocol.AgentArtist:
                {
                    var (call, input) = await artist.RunAsync(ctx, keys, ct);
                    var ack = await steps.ReportAsync(AiProtocol.AgentArtist, 0, null, call, input, ArtistAgent.Prompt, ct);
                    if (ack is null || !call.Succeeded) return;
                    facts.Add(new StepFact(AiProtocol.AgentArtist, 0, true, false));
                    history.Add($"#{steps.LastIndex} Artist: {call.Payload!.Panels.Count} prompt ảnh cho panel.");
                    break;
                }
                case AiProtocol.AgentLetterer:
                {
                    var (call, input) = await letterer.RunAsync(ctx, keys, ct);
                    var ack = await steps.ReportAsync(AiProtocol.AgentLetterer, 0, null, call, input, LettererAgent.Prompt, ct);
                    if (ack is null || !call.Succeeded) return;
                    facts.Add(new StepFact(AiProtocol.AgentLetterer, 0, true, false));
                    history.Add($"#{steps.LastIndex} Letterer: {call.Payload!.Suggestions.Count} đề xuất rút gọn thoại.");
                    break;
                }
                case AiProtocol.AgentProducer:
                {
                    var (call, input) = await producer.RunAsync(ctx, ct);
                    var ack = await steps.ReportAsync(AiProtocol.AgentProducer, 0, null, call, input, ProducerAgent.Prompt, ct);
                    if (ack is null || !call.Succeeded) return;
                    facts.Add(new StepFact(AiProtocol.AgentProducer, 0, true, false));
                    history.Add($"#{steps.LastIndex} Producer: báo cáo tiến độ ({call.Payload!.Risks.Count} rủi ro, {call.Payload.Recommendations.Count} đề xuất).");
                    break;
                }
                default:
                    return;   // hành động kết thúc đã được Backend xử lý ở bước trên
            }
        }

        _logger.LogWarning("Run {RunId}: vượt giới hạn {Max} quyết định điều phối, dừng.", job.RunId, MaxDecisions);
    }

    /// <summary>Đánh số step tuần tự và báo về Backend; chỉ trả ack khi Backend đã lưu.</summary>
    private sealed class StepWriter
    {
        private readonly BackendClient _backend;
        private readonly AiRunJob _job;
        private readonly ILogger _logger;
        private int _next = 1;

        public StepWriter(BackendClient backend, AiRunJob job, ILogger logger)
        {
            _backend = backend;
            _job = job;
            _logger = logger;
        }

        public int LastIndex => _next - 1;

        public async Task<AgentStepAck?> ReportAsync<T>(
            string agent, int round, int? parentIndex, AgentCall<T> call, string inputJson, PromptInfo prompt, CancellationToken ct)
        {
            var report = new AgentStepReport(
                _next, agent, round, parentIndex, call.Succeeded, _job.Provider, _job.Model, prompt.Version, prompt.Ref,
                call.ParametersJson, inputJson, call.OutputJson, call.StartedAtUtc, call.CompletedAtUtc,
                call.PromptTokens, call.CompletionTokens, call.RepairAttempts, call.ErrorCode, call.ErrorMessage);
            _next++;
            return await SendAsync(report, call.Succeeded, ct);
        }

        public async Task ReportFailureAsync(
            string agent, int round, int? parentIndex, string code, string message, PromptInfo prompt, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var report = new AgentStepReport(
                _next, agent, round, parentIndex, false, _job.Provider, _job.Model, prompt.Version, prompt.Ref,
                "{}", "{}", null, now, now, null, null, 0, code, message);
            _next++;
            await SendAsync(report, false, ct);
        }

        private async Task<AgentStepAck?> SendAsync(AgentStepReport report, bool succeeded, CancellationToken ct)
        {
            // Báo step thất bại dù run đang bị huỷ/hết giờ: dùng token riêng ngắn hạn
            using var own = succeeded ? null : new CancellationTokenSource(ReportTimeout);
            var ack = await _backend.ReportStepAsync(_job.RunId, report, own?.Token ?? ct);
            if (ack is null)
                _logger.LogWarning("Run {RunId}: Backend từ chối step {Index} ({Agent}); dừng workflow.", _job.RunId, report.StepIndex, report.Agent);
            else if (!ack.ShouldContinue && succeeded)
                _logger.LogInformation("Run {RunId}: Backend báo {Status} sau step {Index}.", _job.RunId, ack.RunStatus, report.StepIndex);
            // Chỉ trả ack khi workflow còn được phép chạy tiếp (run vẫn Running); ngược lại null = dừng.
            return ack is { ShouldContinue: true } ? ack : null;
        }
    }
}
