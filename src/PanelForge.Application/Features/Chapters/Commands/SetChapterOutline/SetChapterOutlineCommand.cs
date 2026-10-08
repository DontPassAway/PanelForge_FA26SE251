using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;

namespace PanelForge.Application.Features.Chapters.Commands.SetChapterOutline;

/// <summary>V1.2: Producer/Writer ghi outline chương (input của Writer Agent). Chương đã khoá (BR-12) thì không sửa.</summary>
public sealed record SetChapterOutlineCommand(Guid ChapterId, Guid UserId, string? Outline) : IRequest<Result<string?>>;

public sealed class SetChapterOutlineCommandHandler : IRequestHandler<SetChapterOutlineCommand, Result<string?>>
{
    private readonly IPanelForgeDbContext _db;

    public SetChapterOutlineCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<string?>> Handle(SetChapterOutlineCommand cmd, CancellationToken ct)
    {
        var chapter = await _db.Chapters.FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Result<string?>.Failure($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        if (AssignmentRules.ChapterNotEditableReason(chapter) is { } locked)
            return Result<string?>.Failure(locked, ResultErrorCodes.Conflict);

        chapter.SetOutline(cmd.Outline);
        chapter.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<string?>.Success(chapter.Outline);
    }
}
