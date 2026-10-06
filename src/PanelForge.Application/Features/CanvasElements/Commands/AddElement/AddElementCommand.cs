using MediatR;
using PanelForge.Application.Common;

namespace PanelForge.Application.Features.CanvasElements.Commands.AddElement;

/// <summary>
/// Command: Thêm một Element mới vào Page.
/// Thay vì INSERT row, hệ thống APPEND một ElementAddedEvent vào Event Store.
/// </summary>
public sealed record AddElementCommand(
    Guid PageId,
    string ElementType,   // "DialogueBalloon" | "ArtworkLayer" | "NarrationBox" | ...
    double X,
    double Y,
    double Width,
    double Height,
    int ZIndex,
    string? Content,      // Text (dùng cho DialogueBalloon, NarrationBox)
    string? AssetId,      // Asset ID (dùng cho ArtworkLayer)
    Guid AddedByUserId,
    Guid? ScriptLineId = null  // BR-05: balloon/caption/SFX gắn đúng một ScriptLine; speaker lấy từ dòng đó
) : IRequest<Result<AddElementResponse>>;

public sealed record AddElementResponse(
    Guid ElementId,
    Guid PageId,
    string ElementType,
    Guid? ScriptLineId = null,
    Guid? SpeakerId = null
);
