using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Chapters.Commands.DeleteChapter;

public sealed record DeleteChapterCommand(Guid ChapterId, Guid UserId) : IRequest<Result<bool>>;

public sealed class DeleteChapterCommandHandler : IRequestHandler<DeleteChapterCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public DeleteChapterCommandHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> Handle(DeleteChapterCommand command, CancellationToken cancellationToken)
    {
        var chapter = await _dbContext.Chapters
            .FirstOrDefaultAsync(c => c.Id == command.ChapterId, cancellationToken);

        if (chapter == null)
            return Result<bool>.Failure($"Chapter {command.ChapterId} không tồn tại.");

        chapter.DeletedBy = command.UserId.ToString();

        // Xóa thông thường -> DbContext tự động ép thành Soft-delete
        _dbContext.Chapters.Remove(chapter);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
