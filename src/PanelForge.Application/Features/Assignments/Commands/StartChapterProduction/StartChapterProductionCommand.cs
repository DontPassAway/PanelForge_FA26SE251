using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Application.Features.Chapters.Commands.FreezeScript;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Application.Features.TaskGrid.Commands.AssignStageTaskLots;
using PanelForge.Application.Features.TaskGrid.Commands.GenerateStageTasks;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Assignments.Commands.StartChapterProduction;

public sealed record StartChapterProductionCommand(
    Guid ChapterId,
    Guid UserId,
    IReadOnlyList<ProductionPageAssignmentRequest> PageAssignments,
    IReadOnlyList<ProductionPageAssignmentRequest>? LettererAssignments = null,
    bool FirstStageOnly = false)
    : IRequest<Result<StartChapterProductionResultDto>>;

/// <summary>
/// Gate 1 một chạm trên Lưới V3: đóng băng kịch bản (PreProduction → InProduction), sinh ô lưới và giao lô theo dải trang.
/// Dải trang của Artist phủ mọi công đoạn cấp trang dành cho Artist (Pencil, Ink, ...) và cả Lettering khi không giao
/// Letterer phụ việc (Spec §Module 2, BR-31). Ô trong dải đã giao đúng người thì giữ nguyên, nên gọi được cả với chương
/// đã sinh/giao một phần lưới trước Gate 1; công đoạn sau Pencil mà Producer đã giao người khác thì giữ nguyên người đó.
/// </summary>
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
                if (chapter.Status != ChapterStatus.PreProduction)
                    return Fail(
                        "Chương đã bắt đầu sản xuất. Vui lòng sử dụng quy trình khóa lại kịch bản (Refreeze).",
                        ResultErrorCodes.Conflict);

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

                var stages = (await TaskGridRules.LoadStagesAsync(_db, chapter.SeriesId, tct))
                    .Where(s => s.IsEnabled && !(s.IsTerminal && !s.IsApprovalGate))
                    .OrderBy(s => s.StageOrder)
                    .ToList();
                var artistStages = stages.Where(s => s.AllowedRole == WorkspaceRole.Artist).ToList();
                if (artistStages.Count == 0)
                    return Fail("Pipeline của Series chưa có stage Artist đang bật.");
                var firstArtistStage = artistStages[0];
                var lettererStage = stages.FirstOrDefault(s => s.AllowedRole == WorkspaceRole.Letterer);
                var hasLetterers = cmd.LettererAssignments is { Count: > 0 };
                if (hasLetterers && lettererStage is null)
                    return Fail("Pipeline của Series chưa có stage Letterer đang bật.");

                // N2 (Assign Automation): chỉ giao công đoạn Artist đầu tiên, các công đoạn sau để R1 tự giao khi đến lượt.
                // Bỏ nhánh Artist kiêm Letterer, nhưng Letterer Producer đã chỉ định trên form thì vẫn giao (không mất dữ liệu nhập).
                if (cmd.FirstStageOnly) artistStages = [firstArtistStage];

                // Strict = ô đã giao người khác thì báo xung đột (Pencil, Letterer được chỉ định); công đoạn còn lại
                // chỉ nhận ô còn trống để không đè lựa chọn Producer đã làm trên lưới.
                var ranges = cmd.PageAssignments
                    .SelectMany(r => artistStages.Select(s => (Stage: s, Range: r, Strict: s.Id == firstArtistStage.Id)))
                    .Concat(hasLetterers
                        ? cmd.LettererAssignments!.Select(r => (Stage: lettererStage!, Range: r, Strict: true))
                        : lettererStage is null || cmd.FirstStageOnly
                            ? []
                            : cmd.PageAssignments.Select(r => (Stage: lettererStage, Range: r, Strict: false)))
                    .ToList();

                // Kiểm tra người nhận trước mọi thay đổi để lỗi không để lại chương nửa chừng.
                var workspaceId = await _db.Series.AsNoTracking()
                    .Where(s => s.Id == chapter.SeriesId)
                    .Select(s => s.WorkspaceId)
                    .SingleAsync(tct);
                var assigneeIds = ranges.Select(x => x.Range.ArtistId).Distinct().ToList();
                var roles = await _db.WorkspaceMembers.AsNoTracking()
                    .Where(m => m.WorkspaceId == workspaceId && assigneeIds.Contains(m.UserId) &&
                                m.Status == WorkspaceMemberStatus.Active)
                    .ToDictionaryAsync(m => m.UserId, m => m.Role, tct);
                foreach (var (stage, range, _) in ranges)
                {
                    if (!roles.TryGetValue(range.ArtistId, out var role))
                        return Fail($"Người nhận dải trang {range.PageFrom}-{range.PageTo} không phải thành viên đang hoạt động của Workspace.");
                    if (!stage.CanBeHeldBy(role))
                        return Fail($"Dải trang {range.PageFrom}-{range.PageTo} của '{stage.Name}' cần vai trò {stage.AllowedRole}, người nhận đang là {role}.");
                }

                // Reuse D12's submitted-version hash check and freeze implementation.
                var freeze = await new FreezeChapterScriptCommandHandler(_db, _castGate)
                    .Handle(new FreezeChapterScriptCommand(chapter.Id, cmd.UserId), tct);
                if (!freeze.IsSuccess)
                    return Fail(freeze.ErrorMessage!, freeze.ErrorCode ?? ResultErrorCodes.Conflict);

                // Từ đây đã ghi DB: lỗi phải ném ra để transaction rollback (ExecuteInTransactionAsync commit cả khi trả Fail).
                var generated = await new GenerateStageTasksCommandHandler(_db)
                    .Handle(new GenerateStageTasksCommand(chapter.Id, cmd.UserId), tct);
                if (!generated.IsSuccess)
                    throw new Gate1AbortException(generated.ErrorMessage!, generated.ErrorCode ?? ResultErrorCodes.Conflict);

                var cells = await _db.StageTasks.AsNoTracking()
                    .Where(t => t.ChapterId == chapter.Id && t.PageId != null && t.Status != StageTaskStatus.Cancelled)
                    .ToListAsync(tct);
                var lotOwners = await _db.StageTaskLots.AsNoTracking()
                    .Where(l => l.ChapterId == chapter.Id)
                    .ToDictionaryAsync(l => l.Id, l => l.AssigneeUserId, tct);
                var pageIdByNumber = pages.ToDictionary(p => p.PageNumber, p => p.Id);

                var lotRequests = new List<StageTaskLotRequest>();
                foreach (var (stage, range, strict) in ranges)
                {
                    var rangePageIds = Enumerable.Range(range.PageFrom, range.PageTo - range.PageFrom + 1)
                        .Select(n => pageIdByNumber[n]).ToHashSet();
                    var rangeCells = cells.Where(t => t.PipelineStageId == stage.Id && rangePageIds.Contains(t.PageId!.Value)).ToList();
                    if (strict && rangeCells.FirstOrDefault(t => t.LotId is { } lotId && lotOwners[lotId] != range.ArtistId) is not null)
                        throw new Gate1AbortException(
                            $"Dải trang {range.PageFrom}-{range.PageTo} của '{stage.Name}' đã có ô giao cho người khác trên lưới.",
                            ResultErrorCodes.Conflict);
                    var free = rangeCells.Where(t => t.Status == StageTaskStatus.Unassigned).Select(t => t.Id).ToList();
                    if (free.Count > 0)
                        lotRequests.Add(new StageTaskLotRequest(stage.Id, range.ArtistId, free,
                            $"Sản xuất trang {range.PageFrom}-{range.PageTo}", null));
                }

                var lots = new List<StageTaskLotDto>();
                if (lotRequests.Count > 0)
                {
                    var assigned = await new AssignStageTaskLotsCommandHandler(_db)
                        .Handle(new AssignStageTaskLotsCommand(chapter.Id, cmd.UserId, lotRequests, DryRun: false), tct);
                    if (!assigned.IsSuccess)
                        throw new Gate1AbortException(assigned.ErrorMessage!, assigned.ErrorCode ?? ResultErrorCodes.Conflict);
                    lots.AddRange(assigned.Value!.Lots);
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
                        LotCount = lots.Count,
                        TaskCount = lots.Sum(l => l.TaskCount)
                    }),
                    details: "Gate 1: froze script, generated the task grid and assigned production page ranges."));

                await _db.SaveChangesAsync(tct);
                return Result<StartChapterProductionResultDto>.Success(new(
                    ChapterDetailDtoFactory.Create(chapter), [], lots));
            }, ct);
        }
        catch (Gate1AbortException ex)
        {
            return Fail(ex.Message, ex.Code);
        }
        catch (DbUpdateException)
        {
            return Fail(
                "Không thể bắt đầu sản xuất vì dữ liệu giao việc vừa thay đổi. Vui lòng tải lại và thử lại.",
                ResultErrorCodes.Conflict);
        }
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

    private sealed class Gate1AbortException(string message, string code) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
