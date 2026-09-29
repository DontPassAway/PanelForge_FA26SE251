using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.UnitTests.Common;

/// <summary>IUnitOfWork cho unit test: chạy thẳng operation, không mở transaction thật.</summary>
public sealed class PassThroughUnitOfWork : IUnitOfWork
{
    public int ExecutionCount { get; private set; }

    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ExecutionCount++;
        return operation(cancellationToken);
    }
}
