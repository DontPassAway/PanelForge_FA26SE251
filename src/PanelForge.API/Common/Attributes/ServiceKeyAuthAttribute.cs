using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PanelForge.AI.Contracts;
using PanelForge.Application.Features.AiAgents.Models;

namespace PanelForge.API.Common.Attributes;

/// <summary>
/// Xác thực dịch vụ-với-dịch vụ cho API nội bộ (/internal/ai/*): header X-Service-Key khớp AiService:ServiceKey (user-secrets)
/// và chỉ nhận từ loopback. Không dùng JWT của người dùng. Chưa cấu hình khoá → từ chối mọi request.
/// </summary>
public sealed class ServiceKeyAuthAttribute : TypeFilterAttribute
{
    public ServiceKeyAuthAttribute() : base(typeof(ServiceKeyAuthFilter)) { }
}

public sealed class ServiceKeyAuthFilter : IAsyncActionFilter
{
    private readonly AiAgentSettings _settings;

    public ServiceKeyAuthFilter(AiAgentSettings settings) => _settings = settings;

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var remote = context.HttpContext.Connection.RemoteIpAddress;
        if (remote is not null && !IsInternal(remote))
        {
            context.Result = new ObjectResult(new { code = "FORBIDDEN", error = "API nội bộ chỉ nhận từ localhost hoặc mạng nội bộ." }) { StatusCode = 403 };
            return Task.CompletedTask;
        }

        var expected = _settings.ServiceKey;
        var provided = context.HttpContext.Request.Headers[AiProtocol.ServiceKeyHeader].ToString();
        if (string.IsNullOrEmpty(expected) || !FixedTimeEquals(expected, provided))
        {
            context.Result = new UnauthorizedObjectResult(new { code = "UNAUTHORIZED", error = "Khoá dịch vụ không hợp lệ." });
            return Task.CompletedTask;
        }
        return next();
    }

    /// <summary>
    /// TASK-353: loopback hoặc IP mạng riêng (10/8, 172.16/12, 192.168/16, IPv6 ULA fc00::/7) để container
    /// PanelForge.AI trong cùng mạng Docker gọi được. IP công khai vẫn bị chặn; X-Service-Key vẫn bắt buộc.
    /// </summary>
    internal static bool IsInternal(System.Net.IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (System.Net.IPAddress.IsLoopback(ip)) return true;
        var b = ip.GetAddressBytes();
        return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168)
            : (b[0] & 0xFE) == 0xFC;
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}