namespace PanelForge.AI.Contracts;

/// <summary>Sự kiện của một step worker (không phải Supervisor) dùng để tính hành động hợp lệ.</summary>
public sealed record StepFact(string Agent, int Round, bool Succeeded, bool HasBlocking);

/// <summary>
/// Dữ kiện đầu vào do CODE kiểm từ DB (không phải do LLM hay Frontend khai báo). Thiếu dữ kiện mà workflow cần →
/// hành động hợp lệ duy nhất là StopForHuman (kèm lý do rõ ràng).
/// </summary>
public sealed record WorkflowInputs(bool HasScript, bool HasLayout, bool HasArtistPanels)
{
    public static readonly WorkflowInputs All = new(true, true, true);
}

/// <summary>
/// HÀNG RÀO CỨNG của workflow, một nguồn sự thật cho cả hai service: AI service dùng để giới hạn lựa chọn của Supervisor,
/// Backend dùng để từ chối quyết định/step sai trình tự. Supervisor (LLM) quyết định THỨ TỰ trong phạm vi này; không bao giờ vượt ra ngoài.
///
/// Chuỗi hợp lệ theo loại workflow (Finish chỉ xuất hiện sau bước cuối của chính chuỗi đó, không phải "luôn sau Editor"):
///   FullScene   Writer → Continuity (≤ MaxRevisionRounds lượt sửa Writer) → Storyboard → Editor → Finish
///   Script      Writer → Continuity (≤ MaxRevisionRounds lượt sửa Writer) → Finish
///   Storyboard  Continuity → Storyboard → Editor → Finish          (script có sẵn; còn finding chặn → StopForHuman, không có Writer)
///   Editor      Continuity → Editor → Finish
///   Artist      Artist → Finish
/// Script mới/sửa luôn qua Continuity trước khi dùng tiếp; Storyboard chỉ chạy khi Continuity gần nhất sạch.
/// </summary>
public static class WorkflowRules
{
    public const string ActionStopForHuman = "StopForHuman";
    public const string ActionFinish = "Finish";

    public static bool IsTerminal(string action) => action is ActionStopForHuman or ActionFinish;

    /// <summary>Tương thích ngược: workflow đầy đủ (FullScene) với mọi đầu vào sẵn sàng.</summary>
    public static IReadOnlyList<string> AllowedActions(IReadOnlyList<StepFact> workerSteps, int maxRevisionRounds) =>
        AllowedActions(AiProtocol.WorkflowFullScene, WorkflowInputs.All, workerSteps, maxRevisionRounds);

    public static IReadOnlyList<string> AllowedActions(
        string workflowType, WorkflowInputs inputs, IReadOnlyList<StepFact> workerSteps, int maxRevisionRounds)
    {
        if (!AiProtocol.AllWorkflows.Contains(workflowType)) return [ActionStopForHuman];

        if (workerSteps.Count == 0)
            return HasRequiredInput(workflowType, inputs) ? [FirstAgent(workflowType)] : [ActionStopForHuman];

        var last = workerSteps[^1];
        if (!last.Succeeded) return [];

        return workflowType switch
        {
            AiProtocol.WorkflowFullScene => WriterLoop(workerSteps, last, maxRevisionRounds, afterClean: AiProtocol.AgentStoryboard,
                afterStoryboard: AiProtocol.AgentEditor),
            AiProtocol.WorkflowScript => WriterLoop(workerSteps, last, maxRevisionRounds, afterClean: ActionFinish),
            AiProtocol.WorkflowStoryboard => last.Agent switch
            {
                AiProtocol.AgentContinuity when last.HasBlocking => [ActionStopForHuman],
                AiProtocol.AgentContinuity => [AiProtocol.AgentStoryboard],
                AiProtocol.AgentStoryboard => [AiProtocol.AgentEditor],
                AiProtocol.AgentEditor => [ActionFinish],
                _ => [ActionStopForHuman]
            },
            AiProtocol.WorkflowEditor => last.Agent switch
            {
                AiProtocol.AgentContinuity => [AiProtocol.AgentEditor],   // finding chặn là đầu vào của báo cáo review, không cấm review
                AiProtocol.AgentEditor => [ActionFinish],
                _ => [ActionStopForHuman]
            },
            AiProtocol.WorkflowArtist => last.Agent == AiProtocol.AgentArtist ? [ActionFinish] : [ActionStopForHuman],
            _ => [ActionStopForHuman]
        };
    }

    /// <summary>Agent chuyên môn của từng workflow (để UI/Supervisor biết phạm vi; không phải thứ tự chạy).</summary>
    public static IReadOnlyList<string> AgentsOf(string workflowType) => workflowType switch
    {
        AiProtocol.WorkflowFullScene => [AiProtocol.AgentWriter, AiProtocol.AgentContinuity, AiProtocol.AgentStoryboard, AiProtocol.AgentEditor],
        AiProtocol.WorkflowScript => [AiProtocol.AgentWriter, AiProtocol.AgentContinuity],
        AiProtocol.WorkflowStoryboard => [AiProtocol.AgentContinuity, AiProtocol.AgentStoryboard, AiProtocol.AgentEditor],
        AiProtocol.WorkflowEditor => [AiProtocol.AgentContinuity, AiProtocol.AgentEditor],
        AiProtocol.WorkflowArtist => [AiProtocol.AgentArtist],
        _ => []
    };

    public static string FirstAgent(string workflowType) => workflowType switch
    {
        AiProtocol.WorkflowStoryboard or AiProtocol.WorkflowEditor => AiProtocol.AgentContinuity,
        AiProtocol.WorkflowArtist => AiProtocol.AgentArtist,
        _ => AiProtocol.AgentWriter
    };

    public static bool HasRequiredInput(string workflowType, WorkflowInputs i) => workflowType switch
    {
        AiProtocol.WorkflowStoryboard or AiProtocol.WorkflowEditor => i.HasScript,
        AiProtocol.WorkflowArtist => i.HasArtistPanels,
        _ => true
    };

    /// <summary>Vòng Writer ↔ Continuity: sau Continuity có finding chặn → Writer (còn lượt) hoặc StopForHuman; hết lượt chỉ còn StopForHuman.</summary>
    private static IReadOnlyList<string> WriterLoop(
        IReadOnlyList<StepFact> steps, StepFact last, int maxRevisionRounds, string afterClean, string? afterStoryboard = null)
    {
        switch (last.Agent)
        {
            case AiProtocol.AgentWriter:
                return [AiProtocol.AgentContinuity];
            case AiProtocol.AgentContinuity when last.HasBlocking:
                var rounds = steps.Where(s => s.Agent == AiProtocol.AgentWriter).Select(s => s.Round).DefaultIfEmpty(0).Max();
                return rounds < maxRevisionRounds
                    ? [AiProtocol.AgentWriter, ActionStopForHuman]
                    : [ActionStopForHuman];
            case AiProtocol.AgentContinuity:
                return [afterClean];
            case AiProtocol.AgentStoryboard when afterStoryboard is not null:
                return [afterStoryboard];
            case AiProtocol.AgentEditor:
                return [ActionFinish];
            default:
                return [ActionStopForHuman];
        }
    }
}
