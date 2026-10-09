using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.FrameTemplates.Models;

/// <summary>IsBuiltIn = template có sẵn của hệ thống (WorkspaceId null, không xoá được).</summary>
public sealed record FrameTemplateDto(
    Guid Id,
    Guid? WorkspaceId,
    string Name,
    string LayoutFormat,
    bool IsBuiltIn,
    int PanelCount,
    IReadOnlyList<FrameSlot> Slots,
    DateTime CreatedAt,
    string? CreatedBy)
{
    public static FrameTemplateDto From(FrameTemplate t)
    {
        var slots = t.Slots;
        return new FrameTemplateDto(t.Id, t.WorkspaceId, t.Name, t.LayoutFormat.ToString(), t.WorkspaceId is null,
            slots.Count, slots, t.CreatedAt, t.CreatedBy);
    }
}
