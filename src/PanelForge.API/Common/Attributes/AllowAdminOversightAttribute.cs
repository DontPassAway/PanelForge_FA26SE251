namespace PanelForge.API.Common.Attributes;

/// <summary>
/// Cho Administrator ĐỌC endpoint gắn <see cref="RequireWorkspaceRoleAttribute"/> dù không là thành viên Studio.
/// Chỉ áp cho GET (vd Dashboard độc giả, BR-32: Admin là người xem). Thiếu attribute này Admin bị 403 (BR-07).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AllowAdminOversightAttribute : Attribute;
