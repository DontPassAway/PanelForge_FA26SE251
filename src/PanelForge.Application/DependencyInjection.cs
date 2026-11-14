using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PanelForge.Application.Features.CanvasElements.Commands.AddElement;

namespace PanelForge.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Đăng ký MediatR — scan toàn bộ assembly Application
        // để tìm tất cả IRequestHandler<TCommand, TResult>
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        services.AddScoped<Features.Chapters.Services.IChapterCastService>(sp =>
            new Features.Chapters.Services.ChapterCastService(
                sp.GetRequiredService<Common.Interfaces.Persistence.IPanelForgeDbContext>(),
                sp.GetService<Common.Interfaces.Services.IChapterCastInheritanceResolver>()));

        services.AddScoped<Features.Chapters.Services.IChapterCastGate>(sp =>
            new Features.Chapters.Services.ChapterCastGate(
                sp.GetRequiredService<Common.Interfaces.Persistence.IPanelForgeDbContext>(),
                sp.GetService<Features.Chapters.Services.IChapterCastService>(),
                sp.GetService<Common.Interfaces.Services.IChapterCastInheritanceResolver>()));

        return services;
    }
}
