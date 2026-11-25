using Microsoft.Extensions.Configuration;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.Infrastructure.Services;

/// <summary>
/// Lưu file trên đĩa cục bộ dưới <c>AssetStorage:RootPath</c> (mặc định <c>App_Data/assets</c> cạnh ứng dụng).
/// Khoá được ghép vào thư mục gốc rồi kiểm lại để không thoát ra ngoài (chống path traversal).
/// </summary>
public sealed class LocalAssetStorage : IAssetStorage
{
    private readonly string _root;

    public LocalAssetStorage(IConfiguration configuration)
    {
        var configured = configuration["AssetStorage:RootPath"];
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "App_Data", "assets")
            : configured);
    }

    // Đĩa cục bộ không có chỗ lưu metadata → bỏ qua info
    public async Task SaveAsync(string key, byte[] content, AssetObjectInfo? info, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        if (File.Exists(path)) return; // nội dung gắn theo hash nên đã có thì không ghi lại

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Ghi ra file tạm rồi đổi tên để người đọc không bao giờ thấy file dở dang
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(temp, content, cancellationToken);
        try
        {
            File.Move(temp, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
            File.Delete(temp); // hai request cùng nội dung chạy song song: bản kia đã ghi xong
        }
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    private string Resolve(string key)
    {
        var full = Path.GetFullPath(Path.Combine(_root, key));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Storage key không hợp lệ.");
        return full;
    }
}
