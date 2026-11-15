using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Application.Features.Chapters.Commands.FreezeScript;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Application.Features.Chapters.Queries.GetChapterCast;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Commands.StartChapterProduction;

public sealed record StartChapterProductionCommand(
    Guid ChapterId,
    Guid UserId,
    IReadOnlyList<ProductionPageAssignmentRequest> PageAssignments,
    IReadOnlyList<ProductionPageAssignmentRequest>? LettererAssignments = null)
    : IRequest<Result<StartChapterProductionResultDto>>;

public sealed class StartChapterProductionCommandHandler
    : IRequestHandler<StartChapterProductionCommand, Result<StartChapterProductionResultDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChapterCastGate _castGate;

    public StartChapterProductionCommandHandler(
        IPanelForgeDbContext db,
        IUnitOfWork unitOfWork,
        IChapterCastGate castGate)
    {
        _db = db;
        _unitOfWork = unitOfWork;
        _castGate = castGate;
    }

    public async Task<Result<StartChapterProductionResultDto>> Handle(
        StartChapterProductionCommand cmd,
        CancellationToken ct)
    {
        static Result<StartChapterProductionResultDto> Fail(
            string message,
            string code = ResultErrorCodes.Validation,
            object? details = null) =>
            Result<StartChapterProductionResultDto>.Failure(message, code, details);

        if (cmd.PageAssignments is not { Count: > 0 })
            return Fail("Phải giao ít nhất một dải trang cho Artist.");

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async tct =>
            {
                await _db.LockChapterAsync(cmd.ChapterId, tct);

                var chapter = await _db.Chapters
                    .Include(c => c.Scenes)
                    .Include(c => c.Pages)
                    .FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, tct);
                if (chapter is null)
                    return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
                // ponytail: Clean Cutover chặn Gate 1 tạo Assignment V2 trên chương đã dùng lưới; sinh ô lưới theo khoảng trang khi cần.
                if (await AssignmentRules.GridActiveReasonAsync(_db, chapter.Id, tct) is { } grid)
                    return Fail(grid, ResultErrorCodes.Conflict);

                // Gate 1 starts with the single authoritative Bible check. Script comments are deliberately not queried.
                var bibleGate = await _castGate.EvaluateStartAsync(chapter.Id, tct);
                if (!bibleGate.CanStart)
                    return Fail(
                        "Không thể bắt đầu sản xuất do Series Bible chưa sẵn sàng.",
                        ResultErrorCodes.Conflict,
                        bibleGate.Blockers);

                var pages = chapter.Pages.OrderBy(p => p.PageNumber).ToList();
                if (pages.Count == 0)
                    return Fail("Chương chưa có trang để giao việc.");

                if (ValidateRanges(cmd.PageAssignments, pages, "Artist") is { } artistError)
                    return Fail(artistError.Message, artistError.Code);
                if (cmd.LettererAssignments is { Count: > 0 } letterers &&
                    ValidateRanges(letterers, pages, "Letterer") is { } lettererError)
                    return Fail(lettererError.Message, lettererError.Code);

                var pipelineIds = await _db.PipelineDefinitions.AsNoTracking()
                    .Where(d => d.SeriesId == chapter.SeriesId)
                    .Select(d => d.Id)
                    .ToListAsync(tct);
                var stages = await _db.PipelineStages.AsNoTracking()
                    .Where(s => pipelineIds.Contains(s.PipelineDefinitionId) && s.IsEnabled)
                    .OrderBy(s => s.StageOrder)
                    .ToListAsync(tct);
                var artistStage = stages.FirstOrDefault(s => s.AllowedRole == WorkspaceRole.Artist);
                if (artistStage is null)
                    return Fail("Pipeline của Series chưa có stage Artist đang bật.");
                var lettererStage = stages.FirstOrDefault(s => s.AllowedRole == WorkspaceRole.Letterer);
                if (cmd.LettererAssignments is { Count: > 0 } && lettererStage is null)
                    return Fail("Pipeline của Series chưa có stage Letterer đang bật.");

                var workspaceId = await _db.Series.AsNoTracking()
                    .Where(s => s.Id == chapter.SeriesId)
                    .Select(s => s.WorkspaceId)
                    .SingleAsync(tct);

                var batchId = Guid.NewGuid();
                var pending = new List<Assignment>();
                var artistResult = await BuildAssignmentsAsync(
                    chapter, workspaceId, artistStage, WorkspaceRole.Artist,
                    cmd.PageAssignments, pages, cmd.UserId, batchId, pending, true, tct);
                if (!artistResult.IsSuccess)
                    return Fail(artistResult.ErrorMessage!, artistResult.ErrorCode!);
                pending.AddRange(artistResult.Value!);

                if (cmd.LettererAssignments is { Count: > 0 } requestedLetterers)
                {
                    var lettererResult = await BuildAssignmentsAsync(
                        chapter, workspaceId, lettererStage!, WorkspaceRole.Letterer,
                        requestedLetterers, pages, cmd.UserId, batchId, pending, false, tct);
                    if (!lettererResult.IsSuccess)
                        return Fail(lettererResult.ErrorMessage!, lettererResult.ErrorCode!);
                    pending.AddRange(lettererResult.Value!);
                }

                // Reuse D12's submitted-version hash check and freeze implementation.
                var freeze = await new FreezeChapterScriptCommandHandler(_db, _castGate)
                    .Handle(new FreezeChapterScriptCommand(chapter.Id, cmd.UserId), tct);
                if (!freeze.IsSuccess)
                    return Fail(freeze.ErrorMessage!, freeze.ErrorCode ?? ResultErrorCodes.Conflict);

                foreach (var assignment in pending)
                    _db.Assignments.Add(assignment);

                foreach (var group in pending.GroupBy(a => a.PipelineStageId))
                {
                    var stage = stages.Single(s => s.Id == group.Key);
                    await AssignmentBatchPlanner.LinkLaterChapterWorkAsync(
                        _db, chapter.Id, stage, group.ToList(), pending, tct);
                }

                foreach (var assignee in pending.DistinctBy(a => (a.AssigneeUserId, a.AssignedRole)))
                    await AssignmentRules.EnsureSeriesMemberAsync(
                        _db, chapter.SeriesId, assignee.AssigneeUserId, assignee.AssignedRole, tct);

                foreach (var group in pending.GroupBy(a => (a.AssigneeUserId, a.PipelineStageId)))
                {
                    var stageName = stages.Single(s => s.Id == group.Key.PipelineStageId).Name;
                    _db.Notifications.Add(AssignmentNotifications.AssignedBatch(group.ToList(), stageName));
                }

                _db.AuditLogs.Add(AuditLog.Create(
                    "START_CHAPTER_PRODUCTION",
                    nameof(Chapter),
                    chapter.Id.ToString(),
                    workspaceId,
                    cmd.UserId,
                    changesJson: JsonSerializer.Serialize(new
                    {
                        ChapterStatus = ChapterStatus.InProduction.ToString(),
                        AssignmentCount = pending.Count,
                        BatchId = batchId
                    }),
                    details: "Gate 1: froze script, assigned production pages, and started chapter production."));

                await _db.SaveChangesAsync(tct);
                return Result<StartChapterProductionResultDto>.Success(new(
                    ChapterDetailDtoFactory.Create(chapter),
                    pending.Select(AssignmentDto.From).ToList()));
            }, ct);
        }
        catch (DbUpdateException)
        {
            return Fail(
                "Không thể bắt đầu sản xuất vì dữ liệu giao việc vừa thay đổi. Vui lòng tải lại và thử lại.",
                ResultErrorCodes.Conflict);
        }
    }

    private async Task<Result<List<Assignment>>> BuildAssignmentsAsync(
        Chapter chapter,
        Guid workspaceId,
        PipelineStage stage,
        WorkspaceRole role,
        IReadOnlyList<ProductionPageAssignmentRequest> requests,
        IReadOnlyList<Page> pages,
        Guid producerId,
        Guid batchId,
        IReadOnlyList<Assignment> pending,
        bool allowWholeChapter,
        CancellationToken ct)
    {
        var created = new List<Assignment>();
        var wholeChapter = allowWholeChapter && requests.Count == 1 &&
                           requests[0].PageFrom == pages[0].PageNumber &&
                           requests[0].PageTo == pages[^1].PageNumber;

        foreach (var request in requests)
        {
            var pageIds = pages
                .Where(p => p.PageNumber >= request.PageFrom && p.PageNumber <= request.PageTo)
                .Select(p => p.Id)
                .ToList();
            var resolved = await AssignmentBatchPlanner.ResolveAsync(
                _db, chapter, workspaceId, stage.Id, request.ArtistId, pageIds, ct);
            if (!resolved.IsSuccess)
                return Result<List<Assignment>>.Failure(resolved.ErrorMessage!, resolved.ErrorCode);

            var batch = wholeChapter
                ? new AssignmentBatchPlanner.StageBatch(
                    stage, role, [new AssignmentBatchPlanner.Target(WorkflowEntityType.Chapter, chapter.Id, null)])
                : resolved.Value!;
            var plan = await AssignmentBatchPlanner.PlanAsync(_db, chapter.Id, batch, pending.Concat(created).ToList(), ct);
            var conflicts = plan.Where(i => i.ConflictReason is not null).ToList();
            if (conflicts.Count > 0)
                return Result<List<Assignment>>.Failure(
                    AssignmentBatchPlanner.DescribeConflicts(conflicts), ResultErrorCodes.Conflict);

            var brief = wholeChapter
                ? "Sản xuất toàn bộ chương"
                : $"Sản xuất trang {request.PageFrom}-{request.PageTo}";
            created.AddRange(AssignmentBatchPlanner.Build(
                chapter, batch, plan, request.ArtistId, producerId, brief,
                null, null, AssignmentPriority.Medium, batchId));
        }

        return Result<List<Assignment>>.Success(created);
    }

    private static RangeError? ValidateRanges(
        IReadOnlyList<ProductionPageAssignmentRequest> ranges,
        IReadOnlyList<Page> pages,
        string stageName)
    {
        var pageNumbers = pages.Select(p => p.PageNumber).ToHashSet();
        foreach (var range in ranges)
        {
            if (range.ArtistId == Guid.Empty)
                return new("Người nhận việc không hợp lệ.", ResultErrorCodes.Validation);
            if (range.PageFrom > range.PageTo ||
                !pageNumbers.Contains(range.PageFrom) ||
                !pageNumbers.Contains(range.PageTo) ||
                Enumerable.Range(range.PageFrom, range.PageTo - range.PageFrom + 1).Any(n => !pageNumbers.Contains(n)))
                return new(
                    $"Dải trang {range.PageFrom}-{range.PageTo} nằm ngoài các trang của chương.",
                    ResultErrorCodes.Validation);
        }

        for (var i = 0; i < ranges.Count; i++)
        for (var j = i + 1; j < ranges.Count; j++)
        {
            if (ranges[i].PageFrom <= ranges[j].PageTo && ranges[j].PageFrom <= ranges[i].PageTo)
                return new(
                    $"Dải trang {ranges[i].PageFrom}-{ranges[i].PageTo} chồng lấn dải " +
                    $"{ranges[j].PageFrom}-{ranges[j].PageTo} trong cùng stage {stageName}.",
                    ResultErrorCodes.Conflict);
        }

        return null;
    }

    private sealed record RangeError(string Message, string Code);
}
