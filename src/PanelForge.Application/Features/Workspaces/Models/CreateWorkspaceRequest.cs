using System.ComponentModel.DataAnnotations;

namespace PanelForge.Application.Features.Workspaces.Models;

public record CreateWorkspaceRequest
{
    [Required(ErrorMessage = "Tên Workspace là bắt buộc.")]
    [MaxLength(100, ErrorMessage = "Tên Workspace không được vượt quá 100 ký tự.")]
    public string Name { get; init; } = default!;

    /// <summary>D12: phần dung lượng chia từ kho Producer (bỏ trống = hạn mức mặc định). Bỏ qua nếu chưa có kho.</summary>
    [Range(1, long.MaxValue, ErrorMessage = "Dung lượng chia cho Studio phải lớn hơn 0.")]
    public long? StorageQuotaBytes { get; init; }

    /// <summary>D12: số token AI (mua một lần) chia từ kho Producer (bỏ trống = hạn mức mặc định).</summary>
    [Range(1, long.MaxValue, ErrorMessage = "Số token chia cho Studio phải lớn hơn 0.")]
    public long? TokenQuota { get; init; }
}
