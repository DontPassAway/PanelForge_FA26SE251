using System.Text;

namespace PanelForge.AI.Contracts;

/// <summary>Ảnh tham chiếu (character sheet) gửi kèm prompt. Provider không hỗ trợ ảnh đầu vào (Cloudflare text-to-image) sẽ bỏ qua.</summary>
public sealed record ImageReference(string Label, string MimeType, string Base64);

/// <summary>Backend → AI service: sinh MỘT ảnh. ApiKey (Gemini của Workspace) chỉ nằm trong bộ nhớ, không log.</summary>
public sealed record ImageGenRequest(
    string Provider,
    string Model,
    string? ApiKey,
    string Prompt,
    IReadOnlyList<ImageReference> References,
    int SchemaVersion = AiProtocol.SchemaVersion)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"Provider = {Provider}, Model = {Model}, ApiKey = ***, References = {References.Count}");
        return true;
    }
}

/// <summary>SafetyBlocked = provider chặn nội dung: không retry, người dùng sửa prompt rồi sinh lại.</summary>
public sealed record ImageGenResponse(
    bool Succeeded,
    string? ImageBase64,
    string? MimeType,
    bool SafetyBlocked,
    long? PromptTokens,
    long? CompletionTokens,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>Payload AiSuggestion kind Thumbnail. Ảnh đã nằm trong kho (AssetId); Accept chỉ tạo AssetReference phiên bản mới cho panel.</summary>
public sealed record ThumbnailSuggestion(
    int SchemaVersion,
    Guid RunId,
    Guid PanelId,
    Guid PageId,
    Guid StageId,
    Guid AssetId,
    string Sha256,
    Guid ImagePromptRevisionId,
    string Prompt,
    string Provider,
    string Model,
    IReadOnlyList<Guid> ReferenceEntryIds);

public static class ImageProviders
{
    public const string Mock = AiProtocol.ProviderMock;
    public const string Gemini = AiProtocol.ProviderGemini;
    public const string Cloudflare = "Cloudflare";

    public static bool IsKnown(string? p) => p is Mock or Gemini or Cloudflare;
}

/// <summary>Backend → AI service: so MỘT ảnh panel với character sheet của MỘT nhân vật (vision). ApiKey không log.</summary>
public sealed record VisionCompareRequest(
    string Provider,
    string Model,
    string? ApiKey,
    string CharacterName,
    ImageReference CharacterSheet,
    ImageReference Artwork,
    int SchemaVersion = AiProtocol.SchemaVersion)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"Provider = {Provider}, Model = {Model}, ApiKey = ***, CharacterName = {CharacterName}");
        return true;
    }
}

public sealed record VisionDeviation(string Aspect, string Description, string? RegionHint);

/// <summary>Score 0..1 do model chấm; Succeeded = false khi lỗi/không đọc được (không đoán điểm).</summary>
public sealed record VisionCompareResponse(
    bool Succeeded,
    decimal? Score,
    IReadOnlyList<VisionDeviation> Deviations,
    string? SummaryReason,
    string Model,
    long? PromptTokens,
    long? CompletionTokens,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    string? ErrorCode,
    string? ErrorMessage);
