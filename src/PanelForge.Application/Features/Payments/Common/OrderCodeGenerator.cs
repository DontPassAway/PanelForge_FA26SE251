using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Payments.Common;

/// <summary>
/// AUD-BE-10: Sinh mã đơn hàng duy nhất (PF + 10 chữ số, kiểm tra trùng trên DB).
/// Khớp regex SePay webhook (<c>PF\d{6,12}</c>) và tránh lỗi đụng unique index.
/// </summary>
public static class OrderCodeGenerator
{
    private const int DigitCount = 10;
    private const int MaxAttempts = 20;

    public static async Task<string> GenerateUniqueAsync(IPanelForgeDbContext db, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var code = GenerateCandidate();
            var exists = await db.PaymentTransactions.AsNoTracking()
                .AnyAsync(t => t.OrderCode == code, ct);
            if (!exists)
                return code;
        }

        // Fallback cực hiếm: 12 chữ số từ ticks + random
        var suffix = (ulong)(DateTime.UtcNow.Ticks ^ RandomNumberGenerator.GetInt32(int.MaxValue)) % 1_000_000_000_000UL;
        return $"PF{suffix:D12}";
    }

    internal static string GenerateCandidate()
    {
        Span<char> buffer = stackalloc char[2 + DigitCount];
        buffer[0] = 'P';
        buffer[1] = 'F';
        for (var i = 0; i < DigitCount; i++)
        {
            buffer[2 + i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        }
        return new string(buffer);
    }
}
