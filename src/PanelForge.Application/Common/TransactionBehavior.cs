using MediatR;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Common;

/// <summary>
/// Đánh dấu command phải chạy trong một transaction, để <c>LockChapterAsync</c> (SELECT ... FOR UPDATE)
/// thật sự giữ khoá tới khi lưu xong (BR-11). Không có transaction thì Postgres nhả khoá ngay sau câu SELECT.
/// </summary>
public interface ITransactionalCommand;

public sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IUnitOfWork _unitOfWork;
    public TransactionBehavior(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        => request is ITransactionalCommand
            ? _unitOfWork.ExecuteInTransactionAsync(_ => next(), ct)
            : next();
}
