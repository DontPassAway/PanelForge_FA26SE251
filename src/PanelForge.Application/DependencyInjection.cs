using Microsoft.Extensions.DependencyInjection;
using PanelForge.Application.Services;

namespace PanelForge.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        return services;
    }
}
