using PanelForge.AI.Contracts;

namespace PanelForge.Application.Common.Interfaces.Services;

/// <summary>
/// Port gọi sang service PanelForge.AI (vượt ranh giới tiến trình/mạng và phải thay thế được trong test).
/// Không bao giờ ném lỗi vì AI tắt: trả <see cref="AiRunAck"/> với Accepted = false và ErrorCode (AI_UNAVAILABLE...).
/// </summary>
public interface IAiClient
{
    Task<AiRunAck> StartRunAsync(AiRunJob job, CancellationToken cancellationToken = default);

    /// <summary>Copilot sync (không retry). AI tắt/timeout → CopilotResponse.Succeeded = false, ErrorCode AI_UNAVAILABLE.</summary>
    Task<CopilotResponse> RunCopilotAsync(CopilotRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sinh MỘT ảnh (sync, không retry). AI tắt/timeout → Succeeded = false, ErrorCode AI_UNAVAILABLE.</summary>
    Task<ImageGenResponse> GenerateImageAsync(ImageGenRequest request, CancellationToken cancellationToken = default);

    /// <summary>So artwork panel với character sheet (vision, sync, không retry ở Backend). AI tắt/timeout → Succeeded = false.</summary>
    Task<VisionCompareResponse> CompareImagesAsync(VisionCompareRequest request, CancellationToken cancellationToken = default);
}