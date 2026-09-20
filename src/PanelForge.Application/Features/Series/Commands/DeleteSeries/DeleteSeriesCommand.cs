using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Series.Commands.DeleteSeries;

public sealed record DeleteSeriesCommand(Guid SeriesId, Guid UserId) : IRequest<Result<bool>>;

public sealed class DeleteSeriesCommandHandler : IRequestHandler<DeleteSeriesCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public DeleteSeriesCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> Handle(DeleteSeriesCommand command, CancellationToken cancellationToken)
    {
        var series = await _dbContext.Series
            .FirstOrDefaultAsync(s => s.Id == command.SeriesId, cancellationToken);

        if (series == null)
            return Result<bool>.Failure($"Series {command.SeriesId} không tồn tại.");

        series.DeletedBy = command.UserId.ToString();

        // Gọi lệnh Remove chuẩn của EF Core; DbContext sẽ tự động chuyển thành Soft-delete
        _dbContext.Series.Remove(series);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
