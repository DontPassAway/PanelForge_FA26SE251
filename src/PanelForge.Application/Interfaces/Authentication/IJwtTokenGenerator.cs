using PanelForge.Domain.Entities.Auth;

namespace PanelForge.Application.Interfaces.Authentication;

public interface IJwtTokenGenerator
{
    (string Token, DateTime ExpiresAt) GenerateToken(User user);
}
