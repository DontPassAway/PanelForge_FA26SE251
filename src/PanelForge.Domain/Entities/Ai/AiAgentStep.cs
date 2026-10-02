using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Ai;

/// <summary>
/// Một lượt chạy của một agent. Append-only (BR-01, BR-10): không có phương thức sửa, mỗi lượt sửa của Writer là một step riêng.
/// (RunId, StepIndex) là khoá chống lưu trùng khi AI service retry callback.
/// </summary>
public class AiAgentStep : BaseEntity
{
    public Guid RunId { get; private set; }
    public int StepIndex { get; private set; }
    public AiAgentType Agent { get; private set; }
    /// <summary>0 = lượt đầu; k = lượt sửa thứ k (Writer) hoặc lần kiểm tra thứ k (Continuity).</summary>
    public int Round { get; private set; }
    /// <summary>Step mà output của nó là input chính của step này (chuỗi Writer → Continuity → Writer sửa → ...).</summary>
    public Guid? ParentStepId { get; private set; }

    public AiStepStatus Status { get; private set; }
    public string Provider { get; private set; } = default!;
    public string Model { get; private set; } = default!;
    public string PromptVersion { get; private set; } = default!;
    public string PromptRef { get; private set; } = default!;
    public string ParametersJson { get; private set; } = "{}";
    public string InputJson { get; private set; } = "{}";
    public string? OutputJson { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime CompletedAtUtc { get; private set; }
    public long? PromptTokens { get; private set; }
    public long? CompletionTokens { get; private set; }
    public int RepairAttempts { get; private set; }
    public Guid? UsageRecordId { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    private AiAgentStep() { }

    public static AiAgentStep Create(
        Guid runId, int stepIndex, AiAgentType agent, int round, Guid? parentStepId, AiStepStatus status,
        string provider, string model, string promptVersion, string promptRef, string parametersJson,
        string inputJson, string? outputJson, DateTime startedAtUtc, DateTime completedAtUtc,
        long? promptTokens, long? completionTokens, int repairAttempts, string? errorCode, string? errorMessage)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(round);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        return new AiAgentStep
        {
            RunId = runId,
            StepIndex = stepIndex,
            Agent = agent,
            Round = round,
            ParentStepId = parentStepId,
            Status = status,
            Provider = provider,
            Model = model,
            PromptVersion = promptVersion,
            PromptRef = promptRef,
            ParametersJson = parametersJson,
            InputJson = inputJson,
            OutputJson = outputJson,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            RepairAttempts = repairAttempts,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage is { Length: > 2000 } ? errorMessage[..2000] : errorMessage
        };
    }

    public int DurationMs => (int)Math.Max(0, (CompletedAtUtc - StartedAtUtc).TotalMilliseconds);

    /// <summary>Gắn bản ghi usage (chỉ một lần) sau khi IAiUsageService ghi — chống ghi usage hai lần.</summary>
    public void LinkUsageRecord(Guid usageRecordId)
    {
        if (UsageRecordId is not null) throw new InvalidOperationException("Step đã có bản ghi usage.");
        UsageRecordId = usageRecordId;
    }
}