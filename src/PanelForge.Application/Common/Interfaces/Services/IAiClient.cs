using PanelForge.AI.Contracts;

namespace PanelForge.Application.Common.Interfaces.Services;

/// <summary>
/// Port gọi sang service PanelForge.AI (vượt ranh giới tiến trình/mạng và phải thay thế được trong test).
/// Không bao giờ ném lỗi vì AI tắt: trả <see cref="AiRunAck"/> với Accepted = false và ErrorCode (AI_UNAVAILABLE...).
/// </summary>
public interface IAiClient
{
    Task<AiRunAck> StartRunAsync(AiRunJob job, CancellationToken cancellationToken = default);
}