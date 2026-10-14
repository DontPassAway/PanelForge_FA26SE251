namespace PanelForge.API.Common;

/// <summary>
/// Mô tả body <c>{ message }</c> cho Swagger (dùng trong <c>[ProducesResponseType]</c>).
/// Các action vẫn trả object vô danh cùng hình dạng; JSON không đổi.
/// </summary>
public sealed record MessageResponse(string Message);
