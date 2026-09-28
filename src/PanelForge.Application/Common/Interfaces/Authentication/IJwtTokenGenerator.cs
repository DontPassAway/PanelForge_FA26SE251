using PanelForge.Domain.Entities.Identity;

namespace PanelForge.Application.Common.Interfaces.Authentication;

public interface IJwtTokenGenerator
{
    (string Token, DateTime ExpiresAt) GenerateToken(User user);
}
