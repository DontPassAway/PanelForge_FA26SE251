using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Infrastructure.Services;

/// <summary>
/// Lưu file trên Cloudflare R2 (API tương thích S3). Bật bằng <c>AssetStorage:Provider = "R2"</c>.
/// Bucket để private: người dùng không tải thẳng từ R2 mà đi qua API (kiểm quyền + link ký 5 phút, BR-16).
/// Khoá object = <c>Asset.StorageKey</c> do server sinh theo hash nội dung, nên ghi lại cùng khoá là vô hại.
/// </summary>
public sealed class R2AssetStorage : IAssetStorage
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;

    public R2AssetStorage(IAmazonS3 s3, string bucket)
    {
        _s3 = s3;
        _bucket = bucket;
    }

    /// <summary>Đọc <c>AssetStorage:R2:{AccountId|ServiceUrl, Bucket, AccessKeyId, SecretAccessKey}</c>; thiếu thì báo lỗi rõ tên khoá ở lần dùng storage đầu tiên.</summary>
    public static R2AssetStorage FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("AssetStorage:R2");
        string Required(string name) => string.IsNullOrWhiteSpace(section[name])
            ? throw new InvalidOperationException($"Thiếu cấu hình AssetStorage:R2:{name} (đặt bằng user-secrets / biến môi trường).")
            : section[name]!;

        var serviceUrl = !string.IsNullOrWhiteSpace(section["ServiceUrl"])
            ? section["ServiceUrl"]!
            : $"https://{Required("AccountId")}.r2.cloudflarestorage.com";

        var config = new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            AuthenticationRegion = "auto",
            ForcePathStyle = true,
            // R2 không hỗ trợ checksum mặc định mà AWSSDK v4 tự gắn vào mọi request
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        };
        var client = new AmazonS3Client(
            new BasicAWSCredentials(Required("AccessKeyId"), Required("SecretAccessKey")), config);
        return new R2AssetStorage(client, Required("Bucket"));
    }

    public async Task SaveAsync(string key, byte[] content, AssetObjectInfo? info, CancellationToken cancellationToken = default)
    {
        if (await ExistsAsync(key, cancellationToken)) return; // nội dung gắn theo hash nên đã có thì không tải lại

        using var body = new MemoryStream(content, writable: false);
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = body,
            AutoCloseStream = false,
            DisablePayloadSigning = true // R2 không hỗ trợ streaming SigV4
        };
        if (info is not null)
        {
            request.ContentType = info.ContentType;
            // Hiện ở Cloudflare → object → Custom Metadata (x-amz-meta-*)
            foreach (var (name, value) in info.Metadata)
                if (!string.IsNullOrWhiteSpace(value))
                    request.Metadata[name] = ToHeaderValue(value);
        }
        await _s3.PutObjectAsync(request, cancellationToken);
    }

    /// <summary>
    /// Header S3 chỉ nhận ASCII: bỏ dấu tiếng Việt (đ→d), ký tự lạ thành '?', cắt 256 ký tự.
    /// Chỉ để người xem đọc, nên mất dấu chấp nhận được (tên thật vẫn ở DB).
    /// </summary>
    public static string ToHeaderValue(string value)
    {
        var folded = value.Replace('đ', 'd').Replace('Đ', 'D').Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(folded.Length);
        foreach (var c in folded)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c is >= ' ' and <= '~' ? c : '?');
        }
        var result = sb.ToString().Trim();
        return result.Length > 256 ? result[..256] : result;
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _s3.GetObjectAsync(_bucket, key, cancellationToken);
            return response.ResponseStream; // người gọi dispose stream → đóng kết nối
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await _s3.GetObjectMetadataAsync(_bucket, key, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
