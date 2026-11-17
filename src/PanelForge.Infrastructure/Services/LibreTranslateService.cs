using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Infrastructure.Services;

public sealed class LibreTranslateService : ITranslationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<LibreTranslateService> _logger;
    private readonly string _baseUrl;

    public LibreTranslateService(
        HttpClient httpClient,
        ILogger<LibreTranslateService> logger,
        IConfiguration? configuration = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _baseUrl = configuration?["TranslationService:BaseUrl"]?.TrimEnd('/')
            ?? httpClient.BaseAddress?.ToString().TrimEnd('/')
            ?? "http://localhost:5000";
    }

    public async Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            string.Equals(sourceLanguage, targetLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            var payload = new
            {
                q = text,
                source = sourceLanguage,
                target = targetLanguage,
                format = "text"
            };

            var requestUri = _httpClient.BaseAddress != null
                ? new Uri("translate", UriKind.Relative)
                : new Uri($"{_baseUrl}/translate");

            var response = await _httpClient.PostAsJsonAsync(requestUri, payload, cancellationToken: cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cts.Token);
                _logger.LogWarning(
                    "Translation service returned non-success status code {StatusCode}: {ErrorBody}. Returning original text.",
                    response.StatusCode,
                    errorBody);
                return text;
            }

            var result = await response.Content.ReadFromJsonAsync<LibreTranslateResponse>(JsonOptions, cts.Token);
            return !string.IsNullOrEmpty(result?.TranslatedText) ? result.TranslatedText : text;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Translation request timed out for source '{Source}' to '{Target}'. Returning original text.",
                sourceLanguage,
                targetLanguage);
            return text;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Translation service connection error for source '{Source}' to '{Target}'. Returning original text.",
                sourceLanguage,
                targetLanguage);
            return text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Unexpected error occurred during translation. Returning original text.");
            return text;
        }
    }

    private sealed class LibreTranslateResponse
    {
        [JsonPropertyName("translatedText")]
        public string? TranslatedText { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
