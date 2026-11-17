using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.API.Controllers;

/// <summary>
/// Dịch hội thoại/dòng kịch bản tự động bằng LibreTranslate tự lưu trữ (chỉ hỗ trợ en và vi).
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class TranslationsController : ControllerBase
{
    private const int MaxTextLength = 4000;
    private static readonly HashSet<string> SupportedLanguages = new(StringComparer.OrdinalIgnoreCase) { "en", "vi" };

    private readonly ITranslationService _translationService;

    public TranslationsController(ITranslationService translationService)
    {
        _translationService = translationService;
    }

    /// <summary>
    /// POST /api/translations/translate — Dịch văn bản giữa tiếng Anh và tiếng Việt.
    /// </summary>
    [HttpPost("translate")]
    [ProducesResponseType(typeof(TranslateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Translate([FromBody] TranslateRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Yêu cầu dịch không hợp lệ.", message = "Yêu cầu dịch không hợp lệ." });
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new { error = "Văn bản cần dịch không được để trống.", message = "Văn bản cần dịch không được để trống." });
        }

        if (request.Text.Length > MaxTextLength)
        {
            return BadRequest(new { error = $"Văn bản vượt quá giới hạn độ dài ({MaxTextLength} ký tự).", message = $"Văn bản vượt quá giới hạn độ dài ({MaxTextLength} ký tự)." });
        }

        if (string.IsNullOrWhiteSpace(request.SourceLanguage) || !SupportedLanguages.Contains(request.SourceLanguage.Trim()))
        {
            return BadRequest(new { error = "Ngôn ngữ nguồn không được hỗ trợ. Chỉ hỗ trợ 'en' và 'vi'.", message = "Ngôn ngữ nguồn không được hỗ trợ. Chỉ hỗ trợ 'en' và 'vi'." });
        }

        if (string.IsNullOrWhiteSpace(request.TargetLanguage) || !SupportedLanguages.Contains(request.TargetLanguage.Trim()))
        {
            return BadRequest(new { error = "Ngôn ngữ đích không được hỗ trợ. Chỉ hỗ trợ 'en' và 'vi'.", message = "Ngôn ngữ đích không được hỗ trợ. Chỉ hỗ trợ 'en' và 'vi'." });
        }

        var sourceLang = request.SourceLanguage.Trim().ToLowerInvariant();
        var targetLang = request.TargetLanguage.Trim().ToLowerInvariant();

        var translatedText = await _translationService.TranslateAsync(
            request.Text,
            sourceLang,
            targetLang,
            cancellationToken);

        return Ok(new TranslateResponse(translatedText, sourceLang, targetLang));
    }
}

public sealed record TranslateRequest(
    string? Text,
    string? SourceLanguage,
    string? TargetLanguage);

public sealed record TranslateResponse(
    string TranslatedText,
    string SourceLanguage,
    string TargetLanguage);
