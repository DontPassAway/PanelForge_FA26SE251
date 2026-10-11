using PanelForge.Domain.Entities.Identity;

namespace PanelForge.Application.Common.Interfaces.Authentication;

public interface IJwtTokenGenerator
{
    /// <param name="sessionId">Phiên đăng nhập (RefreshToken.SessionId) mà access token này thuộc về, ghi vào claim <c>session_id</c>.</param>
    (string Token, DateTime ExpiresAt) GenerateToken(User user, Guid sessionId);
}
