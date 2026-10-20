using System.Net.Http.Json;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Infrastructure.ExternalServices;

/// <summary>Client mỏng sang service Python PanelForge.PanelSplitter (POST /split, multipart "file").</summary>
public sealed class PanelSplitterClient : IPanelSplitter
{
    private readonly HttpClient _http;

    public PanelSplitterClient(HttpClient http) => _http = http;

    public async Task<PanelSplitResult> SplitAsync(Stream image, string fileName, bool rightToLeft, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent { { new StreamContent(image), "file", fileName } };

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync($"split?rtl={(rightToLeft ? "true" : "false")}", form, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("Service tách panel không chạy.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Service tách panel trả lỗi (HTTP {(int)response.StatusCode}): {await response.Content.ReadAsStringAsync(cancellationToken)}");

            return await response.Content.ReadFromJsonAsync<PanelSplitResult>(cancellationToken)
                ?? throw new InvalidOperationException("Service tách panel trả về rỗng.");
        }
    }
}
