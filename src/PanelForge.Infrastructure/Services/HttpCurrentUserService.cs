using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PanelForge.Application.Interfaces;

namespace PanelForge.Infrastructure.Services;

public sealed class HttpCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentUserService(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var raw = Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public string? Email
        => Principal?.FindFirstValue(ClaimTypes.Email)
           ?? Principal?.FindFirstValue(JwtRegisteredClaimNames.Email);

    public string? IpAddress
        => _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
