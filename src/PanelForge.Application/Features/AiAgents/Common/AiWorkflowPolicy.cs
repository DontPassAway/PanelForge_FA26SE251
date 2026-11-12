using PanelForge.AI.Contracts;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Common;

/// <summary>
/// Chính sách phân quyền theo loại workflow/loại đề xuất — MỘT nguồn sự thật cho RunWorkflow, ReviewSuggestion và API availability.
/// Quyền luôn do Backend xác định từ user + membership; không bao giờ lấy role hay danh sách agent từ client.
///
/// Producer không mặc nhiên có mọi quyền nội dung: Producer chỉ được chạy workflow Script/Storyboard
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
        AiWorkflowType.Editor => [WorkspaceRole.Producer, WorkspaceRole.Editor],
        _ => []
    };

    public static WorkspaceRole[] ReviewRoles(AiSuggestionKind kind) => kind switch
    {
        AiSuggestionKind.Script => [WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Editor],
        AiSuggestionKind.Layout => [WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Artist, WorkspaceRole.Editor],
        AiSuggestionKind.ImagePrompt => [WorkspaceRole.Producer, WorkspaceRole.Artist, WorkspaceRole.Editor],
        AiSuggestionKind.SeriesMetadata => [WorkspaceRole.Producer, WorkspaceRole.Publisher],
        AiSuggestionKind.Thumbnail => [WorkspaceRole.Producer, WorkspaceRole.Artist],
        AiSuggestionKind.Lettering or AiSuggestionKind.LineRewrite =>
            [WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Letterer, WorkspaceRole.Editor],
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
        _ => ["Report"]
    };

    /// <summary>Vai trò được gọi từng task Copilot (Backend tự xác định, không lấy từ client).</summary>
    public static WorkspaceRole[] CopilotRoles(string task) => task switch
    {
        CopilotTasks.WriterRewrite => [WorkspaceRole.Producer, WorkspaceRole.Writer],
        CopilotTasks.LettererShorten => [WorkspaceRole.Letterer],
        // Publisher chỉ kiểm duyệt thuần túy, không dùng AI → chỉ Producer xin gợi ý metadata.
        CopilotTasks.PublisherMetadata => [WorkspaceRole.Producer],
        _ => []
    };

    /// <summary>
    /// Nhiệm vụ hiện trong danh sách cho người dùng chọn. Không hiện: Copilot/Thumbnail (gọi sync từ màn làm việc), Letterer (đã thay bằng
    /// Copilot letterer.shorten) và Producer (số liệu tiến độ xem thẳng qua GET /series/{id}/progress + lock-gate); enum giữ để đọc run cũ; FullScene (= Script → Storyboard → Editor gộp lại; chỉ hiện khi bật chế độ demo).
    /// </summary>
    public static bool IsListed(AiWorkflowType type, bool showFullScene) => type switch
    {
        AiWorkflowType.Copilot or AiWorkflowType.Thumbnail or AiWorkflowType.Letterer or AiWorkflowType.Producer => false,
        AiWorkflowType.FullScene => showFullScene,
        _ => true
    };

    public static bool NeedsScene(AiWorkflowType type) => type != AiWorkflowType.Producer;

    public static string Title(AiWorkflowType type) => type switch
    {
        AiWorkflowType.FullScene => "Scene đầy đủ (script → storyboard → review)",
        AiWorkflowType.Script => "Viết script",
        AiWorkflowType.Storyboard => "Storyboard từ script có sẵn",
        AiWorkflowType.Artist => "Prompt ảnh cho panel",
        AiWorkflowType.Editor => "Review biên tập",
        _ => type.ToString()
    };

    public static string Description(AiWorkflowType type) => type switch
    {
        AiWorkflowType.FullScene => "Writer viết, Continuity kiểm tra với Bible (tối đa 2 lượt sửa), rồi Storyboard chia panel và Editor viết báo cáo review.",
        AiWorkflowType.Script => "Writer viết/sửa script, Continuity kiểm tra với Bible; kết thúc khi script sạch hoặc dừng chờ người nếu còn finding chặn.",
        AiWorkflowType.Storyboard => "Continuity kiểm tra script hiện có rồi Storyboard chia Page/Panel và Editor review. Chỉ áp dụng được vào Scene chưa có Page.",
        AiWorkflowType.Artist => "Artist soạn prompt ảnh cho từng panel của layout đã duyệt, dùng Bible hiệu lực tại chương và style rules. Không sinh ảnh.",
        AiWorkflowType.Editor => "Continuity kiểm tra script có sẵn và Editor tổng hợp báo cáo review (kèm layout đã duyệt nếu có). Báo cáo tham khảo, không có Accept.",
        _ => "Nhiệm vụ đã ngừng hỗ trợ."
    };
}
