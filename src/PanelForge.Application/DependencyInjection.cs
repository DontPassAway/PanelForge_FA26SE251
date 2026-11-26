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
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            cfg.AddOpenBehavior(typeof(Common.TransactionBehavior<,>));
        });

        services.AddScoped<Features.Chapters.Services.IChapterCastService, Features.Chapters.Services.ChapterCastService>();
        services.AddScoped<Features.Chapters.Services.IChapterCastGate, Features.Chapters.Services.ChapterCastGate>();
        services.AddSingleton<Features.Chapters.Services.IScriptImpactClassifier, Features.Chapters.Services.ScriptImpactClassifier>();
        services.AddScoped<Features.Panels.Services.PageLayoutReplacer>();

        return services;
    }
}
