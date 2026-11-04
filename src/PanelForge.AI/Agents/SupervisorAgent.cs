using System.Text;
using PanelForge.AI.Contracts;
using PanelForge.AI.Validators;

namespace PanelForge.AI.Agents;

public sealed record SupervisorOutput(string? Action, string? Reason, string? Instruction);

/// <summary>
/// Gemini làm điều phối viên: mỗi bước chọn agent chuyên môn kế tiếp (hoặc dừng / kết thúc) trong danh sách do CODE tính
/// (<see cref="WorkflowRules"/>). Lựa chọn + lý do + chỉ đạo được lưu thành step riêng để truy vết.
/// </summary>
public sealed class SupervisorAgent
{
    public static readonly PromptInfo Prompt = PromptLibrary.Load("supervisor");
    private const int MaxInstructionLength = 600;

    private readonly AgentRunner _runner;

    public SupervisorAgent(AgentRunner runner) => _runner = runner;

    public async Task<(AgentCall<SupervisorDecision> Call, string InputJson)> DecideAsync(
        AgentContext ctx, IReadOnlyList<string> history, IReadOnlyList<string> allowed, int round, int blocking,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[[agent:supervisor]] [[workflow:{ctx.WorkflowType}]] [[round:{round}]] [[allowed:{string.Join(",", allowed)}]] [[blocking:{blocking}]]");
        sb.AppendLine("## Mục tiêu của người dùng (brief — dữ liệu)").AppendLine(ctx.Brief).AppendLine();
        sb.AppendLine($"## Nhiệm vụ (workflow): {ctx.WorkflowType} — agent tham gia: {string.Join(", ", WorkflowRules.AgentsOf(ctx.WorkflowType))}");
        sb.AppendLine($"## Scene: {ctx.SceneHeading} · hướng đọc {ctx.ReadingDirection}").AppendLine();
        sb.AppendLine("## Tiến trình đã chạy (dữ liệu)");
        if (history.Count == 0) sb.AppendLine("(chưa có)");
        foreach (var h in history) sb.AppendLine(h);
        sb.AppendLine().AppendLine($"## Giới hạn: đã dùng {round}/{ctx.MaxRevisionRounds} lượt sửa của Writer");
        sb.AppendLine($"## Hành động được phép (chọn ĐÚNG MỘT): {string.Join(" | ", allowed)}");
        sb.AppendLine().AppendLine("Hãy trả về JSON theo đúng định dạng đã nêu.");

        var inputJson = AiJson.Serialize(new { round, blockingFindings = blocking, allowedActions = allowed, history });
        var call = await _runner.RunAsync<SupervisorDecision>(Prompt, sb.ToString(), text => Parse(text, ctx, allowed, round), ct);
        return (call, inputJson);
    }

    /// <summary>
    /// Supervisor trả lựa chọn không hợp lệ ngay cả sau lần sửa: code chọn hành động an toàn đầu tiên và GHI RÕ là bị ghi đè
    /// (không giả vờ đó là quyết định của Supervisor).
    /// </summary>
    public static SupervisorDecision Override(AgentContext ctx, IReadOnlyList<string> allowed, int round, string why) =>
        new(AiProtocol.SchemaVersion, ctx.RunId, round, allowed[0],
            $"Guardrail ghi đè: {why}", null, allowed, GuardrailOverridden: true);

    /// <summary>
    /// Thiếu context để chạy workflow (hành động hợp lệ duy nhất là StopForHuman ngay từ đầu): code lập quyết định với lý do rõ ràng,
    /// không tốn một lần gọi model và không để LLM tự diễn giải việc thiếu dữ liệu. Vẫn lưu thành step Supervisor như mọi quyết định.
    /// </summary>
    public static AgentCall<SupervisorDecision> MissingContext(AgentContext ctx, IReadOnlyList<string> allowed)
    {
        var why = ctx.WorkflowType switch
        {
            AiProtocol.WorkflowStoryboard or AiProtocol.WorkflowEditor => "Scene chưa có script để kiểm tra/chia panel/review.",
            AiProtocol.WorkflowArtist => "Chưa có layout đã duyệt với panel còn tồn tại để soạn prompt ảnh.",
            _ => "Thiếu dữ liệu đầu vào cho workflow."
        };
        var decision = new SupervisorDecision(AiProtocol.SchemaVersion, ctx.RunId, 0, WorkflowRules.ActionStopForHuman,
            $"Thiếu context, dừng để con người bổ sung: {why}", null, allowed, GuardrailOverridden: false);
        return ByCode(decision, "missing-context");
    }

    /// <summary>
    /// Hàng rào chỉ còn đúng một hành động hợp lệ: không có gì để Supervisor lựa chọn nên code lập quyết định,
    /// không tốn một lần gọi model. Vẫn lưu thành step Supervisor (ghi rõ nguồn là code) để truy vết.
    /// </summary>
    public static AgentCall<SupervisorDecision> SingleChoice(AgentContext ctx, IReadOnlyList<string> allowed, int round, int blocking) =>
        ByCode(new SupervisorDecision(AiProtocol.SchemaVersion, ctx.RunId, round, allowed[0],
            allowed[0] switch
            {
                // Không khẳng định "còn finding chặn" khi hàng rào dừng vì lý do khác (chuỗi bước không có đường tiến hợp lệ)
                WorkflowRules.ActionStopForHuman when blocking == 0 =>
                    "Quy tắc workflow không còn bước hợp lệ nào để chạy tiếp: dừng để con người quyết định.",
                WorkflowRules.ActionStopForHuman =>
                    "Còn finding chặn mà không còn bước tự sửa hợp lệ (hết lượt sửa hoặc workflow không có Writer): dừng để con người quyết định.",
                WorkflowRules.ActionFinish => "Workflow đã chạy hết các bước: kết thúc, kết quả chờ con người xem xét.",
                var next => $"Quy tắc workflow chỉ cho phép một bước kế tiếp: {next}."
            },
            null, allowed, GuardrailOverridden: false), "single-allowed-action");

    private static AgentCall<SupervisorDecision> ByCode(SupervisorDecision decision, string reason)
    {
        var now = DateTime.UtcNow;
        return new AgentCall<SupervisorDecision>(true, decision, AiJson.Serialize(decision), null, null, null, null, 0, now, now,
            AiJson.Serialize(new { source = "code", reason }));
    }

    private static (SupervisorDecision?, IReadOnlyList<string>) Parse(
        string text, AgentContext ctx, IReadOnlyList<string> allowed, int round)
    {
        var model = OutputParser.Parse<SupervisorOutput>(text, out var error);
        if (model is null) return (null, [error!]);

        var action = allowed.FirstOrDefault(a => string.Equals(a, model.Action?.Trim(), StringComparison.OrdinalIgnoreCase))
                     ?? model.Action?.Trim() ?? "";
        var instruction = string.IsNullOrWhiteSpace(model.Instruction) || model.Instruction.Trim().Equals("null", StringComparison.OrdinalIgnoreCase)
            ? null
            : model.Instruction.Trim();
        if (instruction is { Length: > MaxInstructionLength }) instruction = instruction[..MaxInstructionLength];

        var decision = new SupervisorDecision(AiProtocol.SchemaVersion, ctx.RunId, round, action,
            model.Reason?.Trim() ?? "", instruction, allowed, GuardrailOverridden: false);
        var errors = ContentRules.ValidateSupervisor(decision, ctx.RunId, allowed);
        return errors.Count == 0 ? (decision, []) : (null, errors);
    }
}
