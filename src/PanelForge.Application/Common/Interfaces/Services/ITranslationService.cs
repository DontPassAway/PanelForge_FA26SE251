namespace PanelForge.Application.Common.Interfaces.Services;

public sealed record TranslationResult(string Text, bool IsFallback = false);

public interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default);
}
