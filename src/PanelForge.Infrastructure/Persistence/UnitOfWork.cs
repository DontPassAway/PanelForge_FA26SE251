using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Infrastructure.Persistence;

/// <summary>
/// Mở transaction trên connection của EF Core. MartenPageRepository phát hiện transaction này
/// (Database.CurrentTransaction) và enlist Marten session vào cùng transaction.
/// Chạy qua ExecutionStrategy vì DbContext bật EnableRetryOnFailure.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly PanelForgeDbContext _dbContext;

    public UnitOfWork(PanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        // Đã ở trong transaction (gọi lồng) → dùng lại transaction bên ngoài
        if (_dbContext.Database.CurrentTransaction is not null)
            return await operation(cancellationToken);

        var strategy = _dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

            var result = await operation(ct);

            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);
    }
}
