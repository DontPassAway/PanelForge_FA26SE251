using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Common;

/// <summary>
/// Chính sách phân quyền theo loại workflow/loại đề xuất — MỘT nguồn sự thật cho RunWorkflow, ReviewSuggestion và API availability.
/// Quyền luôn do Backend xác định từ user + membership; không bao giờ lấy role hay danh sách agent từ client.
///
/// Producer không mặc nhiên có mọi quyền nội dung: Producer chỉ được chạy workflow của chính mình (báo cáo tiến độ), workflow Script/Storyboard
/// (giữ đúng chính sách của demo hiện có: Producer điều phối Series) và báo cáo Editor (chỉ đọc). Artist/Letterer là việc riêng của vai trò đó.
/// Quyết định Accept/Reject vẫn thuộc Producer (nghiệm thu) + vai trò sở hữu nội dung + Editor (xem docs/roles/role-workflows.md).
/// </summary>
internal static class AiWorkflowPolicy
{
    public static WorkspaceRole[] RunRoles(AiWorkflowType type) => type switch
    {
        AiWorkflowType.FullScene or AiWorkflowType.Script => [WorkspaceRole.Producer, WorkspaceRole.Writer],
        AiWorkflowType.Storyboard => [WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Artist],
        AiWorkflowType.Artist => [WorkspaceRole.Artist],
        AiWorkflowType.Letterer => [WorkspaceRole.Letterer],
        AiWorkflowType.Editor => [WorkspaceRole.Producer, WorkspaceRole.Editor],
        _ => [WorkspaceRole.Producer]
    };

    public static WorkspaceRole[] ReviewRoles(AiSuggestionKind kind) => kind switch
    {
        AiSuggestionKind.Script => [WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Editor],
        AiSuggestionKind.Layout => [WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Artist, WorkspaceRole.Editor],
        AiSuggestionKind.ImagePrompt => [WorkspaceRole.Producer, WorkspaceRole.Artist, WorkspaceRole.Editor],
        AiSuggestionKind.Lettering => [WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Letterer, WorkspaceRole.Editor],
        _ => []
    };

    /// <summary>Workflow tạo đề xuất áp dụng vào nội dung → cần chapter InProduction (BR-12) và kiểm tra assignment.</summary>
    public static bool ProducesApplicableSuggestions(AiWorkflowType type) => ResultKinds(type).Any(k => k != "Report");

    public static string[] ResultKinds(AiWorkflowType type) => type switch
    {
        AiWorkflowType.FullScene => ["Script", "Layout"],
        AiWorkflowType.Script => ["Script"],
        AiWorkflowType.Storyboard => ["Layout"],
        AiWorkflowType.Artist => ["ImagePrompt"],
        AiWorkflowType.Letterer => ["Lettering"],
        _ => ["Report"]
    };

    public static bool NeedsScene(AiWorkflowType type) => type != AiWorkflowType.Producer;

    public static string Title(AiWorkflowType type) => type switch
    {
        AiWorkflowType.FullScene => "Scene đầy đủ (script → storyboard → review)",
        AiWorkflowType.Script => "Viết script",
        AiWorkflowType.Storyboard => "Storyboard từ script có sẵn",
        AiWorkflowType.Artist => "Prompt ảnh cho panel",
        AiWorkflowType.Letterer => "Rút gọn thoại cho bubble",
        AiWorkflowType.Editor => "Review biên tập",
        _ => "Báo cáo tiến độ"
    };

    public static string Description(AiWorkflowType type) => type switch
    {
        AiWorkflowType.FullScene => "Writer viết, Continuity kiểm tra với Bible (tối đa 2 lượt sửa), rồi Storyboard chia panel và Editor viết báo cáo review.",
        AiWorkflowType.Script => "Writer viết/sửa script, Continuity kiểm tra với Bible; kết thúc khi script sạch hoặc dừng chờ người nếu còn finding chặn.",
        AiWorkflowType.Storyboard => "Continuity kiểm tra script hiện có rồi Storyboard chia Page/Panel và Editor review. Chỉ áp dụng được vào Scene chưa có Page.",
        AiWorkflowType.Artist => "Artist soạn prompt ảnh cho từng panel của layout đã duyệt, dùng Bible hiệu lực tại chương và style rules. Không sinh ảnh.",
        AiWorkflowType.Letterer => "Letterer đề xuất rút gọn thoại giữ ý nghĩa và giọng nhân vật theo giới hạn chữ ước lượng của hệ thống. Người duyệt mới áp dụng.",
        AiWorkflowType.Editor => "Continuity kiểm tra script có sẵn và Editor tổng hợp báo cáo review (kèm layout đã duyệt nếu có). Báo cáo tham khảo, không có Accept.",
        _ => "Producer viết báo cáo tiến độ, rủi ro và đề xuất xử lý từ số liệu assignment/deadline do Backend tính. Báo cáo tham khảo, không tự giao việc hay chuyển stage."
    };
}
