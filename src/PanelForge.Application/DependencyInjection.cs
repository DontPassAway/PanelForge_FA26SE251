using Microsoft.Extensions.DependencyInjection;

namespace PanelForge.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Các dịch vụ thuộc tầng Application sẽ được đăng ký tại đây
        return services;
    }
}
