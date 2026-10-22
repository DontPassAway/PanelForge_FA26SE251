using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// Đại diện cho một thành viên được chỉ định tham gia vào một dự án Series cụ thể với vai trò chuyên môn.
/// </summary>
public class SeriesMember : BaseEntity
{
    public Guid SeriesId { get; private set; }
    public Guid UserId { get; private set; }
    public WorkspaceRole Role { get; private set; }
    public DateTime AssignedAt { get; private set; } = DateTime.UtcNow;

    public Series Series { get; private set; } = default!;
    public User User { get; private set; } = default!;

    private SeriesMember() { }

    public static SeriesMember Create(Guid seriesId, Guid userId, WorkspaceRole role)
    {
        return new SeriesMember
        {
            SeriesId = seriesId,
            UserId = userId,
            Role = role,
            AssignedAt = DateTime.UtcNow
        };
    }

    public void ChangeRole(WorkspaceRole newRole)
    {
        Role = newRole;
    }
}
