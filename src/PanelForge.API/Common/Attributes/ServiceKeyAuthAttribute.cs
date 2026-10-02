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
        if (remote is not null && !System.Net.IPAddress.IsLoopback(remote))
        {
            context.Result = new ObjectResult(new { code = "FORBIDDEN", error = "API nội bộ chỉ nhận từ localhost." }) { StatusCode = 403 };
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

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}