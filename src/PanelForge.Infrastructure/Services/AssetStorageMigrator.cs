using System.Security.Cryptography;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Infrastructure.Services;

/// <summary>
/// Chép ảnh đang lưu trên đĩa (LocalAssetStorage) sang kho khác (R2). Chạy lại an toàn: khoá đã có thì kho đích bỏ qua.
/// Mỗi file phải có SHA-256 khớp tên file (khoá gắn theo hash) – lệch thì báo lỗi, không chép.
/// </summary>
public static class AssetStorageMigrator
{
    public sealed record Summary(int Copied, int Invalid, int Failed);

    public static async Task<Summary> RunAsync(
        string sourceRoot, IAssetStorage? target, TextWriter log, CancellationToken cancellationToken = default)
    {
        var dryRun = target is null; // không có kho đích = chỉ kiểm hash và liệt kê
        int copied = 0, invalid = 0, failed = 0;
        var root = Path.GetFullPath(sourceRoot);

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue; // file ghi dở của LocalAssetStorage

            var key = Path.GetRelativePath(root, path).Replace('\\', '/');
            try
            {
                var content = await File.ReadAllBytesAsync(path, cancellationToken);
                var hash = Convert.ToHexStringLower(SHA256.HashData(content));
                if (!string.Equals(hash, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase))
                {
                    invalid++;
                    await log.WriteLineAsync($"[SAI HASH] {key}");
                    continue;
                }

                if (target is not null)
                {
                    await target.SaveAsync(key, content, info: null, cancellationToken);
                    await using var check = await target.OpenReadAsync(key, cancellationToken)
                        ?? throw new InvalidOperationException("không đọc lại được sau khi chép");
                }
                copied++;
                await log.WriteLineAsync($"[{(dryRun ? "SẼ CHÉP" : "OK")}] {key} ({content.Length:N0} byte)");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                await log.WriteLineAsync($"[LỖI] {key}: {ex.Message}");
            }
        }

        return new Summary(copied, invalid, failed);
    }
}
