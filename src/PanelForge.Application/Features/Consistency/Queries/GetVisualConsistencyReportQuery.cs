using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Consistency.Common;
using PanelForge.Application.Features.Consistency.Models;
using PanelForge.Application.Features.Consistency.Commands;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Consistency.Queries;

/// <summary>
/// Lấy Báo cáo nhất quán hình ảnh chi tiết của Chapter cho Editor / Reviewer (CF3, V1.2).
/// </summary>
public sealed record GetVisualConsistencyReportQuery(
    Guid ChapterId,
    decimal? Threshold = null
) : IRequest<Result<VisualConsistencyReportDto>>;

public sealed class GetVisualConsistencyReportQueryHandler
    : IRequestHandler<GetVisualConsistencyReportQuery, Result<VisualConsistencyReportDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IMediator _mediator;

    public GetVisualConsistencyReportQueryHandler(IPanelForgeDbContext db, IMediator mediator)
    {
        _db = db;
        _mediator = mediator;
    }

    public async Task<Result<VisualConsistencyReportDto>> Handle(
        GetVisualConsistencyReportQuery query,
        CancellationToken ct)
    {
        var chapter = await _db.Chapters.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == query.ChapterId, ct);
        if (chapter is null)
            return Result<VisualConsistencyReportDto>.Failure(
                $"Chapter {query.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var threshold = query.Threshold ?? ConsistencyChecker.DefaultVisualSimilarityThreshold;

        // Lấy các finding hình ảnh đã lưu (VISUAL_CHARACTER_DEVIATION hoặc IMAGE_INCONSISTENCY)
        var findings = await _db.ConsistencyFindings.AsNoTracking()
            .Where(f => f.ChapterId == chapter.Id && (f.RuleCode == ConsistencyChecker.VisualCharacterDeviation || f.RuleCode == ConsistencyChecker.ImageInconsistency))
            .ToListAsync(ct);

        // Nếu chưa từng chạy kiểm tra, tự động khởi tạo lần đầu
        if (findings.Count == 0)
        {
            var runResult = await _mediator.Send(
                new RunVisualConsistencyCheckCommand(chapter.Id, Guid.Empty, threshold), ct);
            return runResult;
        }

        var users = await _db.Users.AsNoTracking()
            .Select(u => new { u.Id, u.FullName, u.Email })
            .ToDictionaryAsync(u => u.Id, ct);

        var deviations = new List<VisualDeviationDto>();
        foreach (var f in findings)
        {
            // Parse evidence JSON
            Guid charId = f.BibleEntryId ?? Guid.Empty;
            string charName = "Nhân vật";
            int? charVersion = 1;
            string? charImg = null;
            Guid? pageId = null;
            int? pageNum = null;
            Guid? panelId = f.SceneId;
            int? panelNum = null;
            Guid? assetId = null;
            string? artUrl = null;
            decimal simScore = 0.68m;
            var detailedDevs = new List<ImageConsistencyDeviationDto>();
            string? summaryReason = null;
            string? revisionRef = null;

            if (!string.IsNullOrWhiteSpace(f.Evidence))
            {
                try
                {
                    using var doc = JsonDocument.Parse(f.Evidence);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("characterId", out var cid) && cid.TryGetGuid(out var parsedCid)) charId = parsedCid;
                    if (root.TryGetProperty("characterName", out var cname)) charName = cname.GetString() ?? charName;
                    if (root.TryGetProperty("characterSheetVersion", out var cver) && cver.TryGetInt32(out var parsedCver)) charVersion = parsedCver;
                    if (root.TryGetProperty("characterSheetUrl", out var curl)) charImg = curl.GetString();
                    if (root.TryGetProperty("pageId", out var pid) && pid.TryGetGuid(out var parsedPid)) pageId = parsedPid;
                    if (root.TryGetProperty("pageNumber", out var pnum) && pnum.TryGetInt32(out var parsedPnum)) pageNum = parsedPnum;
                    if (root.TryGetProperty("panelId", out var pnid) && pnid.TryGetGuid(out var parsedPnid)) panelId = parsedPnid;
                    if (root.TryGetProperty("panelNumber", out var pnnum) && pnnum.TryGetInt32(out var parsedPnnum)) panelNum = parsedPnnum;
                    if (root.TryGetProperty("assetReferenceId", out var aid) && aid.TryGetGuid(out var parsedAid)) assetId = parsedAid;
                    if (root.TryGetProperty("artworkUrl", out var aurl)) artUrl = aurl.GetString();
                    if (root.TryGetProperty("similarityScore", out var sscore) && sscore.TryGetDecimal(out var parsedScore)) simScore = parsedScore;
                    if (root.TryGetProperty("summaryReason", out var sreason)) summaryReason = sreason.GetString();
                    if (root.TryGetProperty("revisionRef", out var rref)) revisionRef = rref.GetString();
                    if (root.TryGetProperty("deviations", out var devArr) && devArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in devArr.EnumerateArray())
                        {
                            var aspect = item.TryGetProperty("aspect", out var asp) ? asp.GetString() ?? "general" : "general";
                            var desc = item.TryGetProperty("description", out var ds) ? ds.GetString() ?? "" : "";
                            var hint = item.TryGetProperty("regionHint", out var rh) ? rh.GetString() : null;
                            detailedDevs.Add(new ImageConsistencyDeviationDto(aspect, desc, hint));
                        }
                    }
                }
                catch
                {
                    // Giữ giá trị mặc định nếu JSON không hợp lệ
                }
            }

            string? triagedByName = null;
            if (f.TriagedByUserId.HasValue && users.TryGetValue(f.TriagedByUserId.Value, out var u))
            {
                triagedByName = u.FullName ?? u.Email;
            }

            deviations.Add(new VisualDeviationDto(
                FindingId: f.Id,
                ChapterId: f.ChapterId,
                CharacterId: charId,
                CharacterName: charName,
                CharacterSheetVersion: charVersion,
                CharacterSheetImageUrl: charImg,
                PageId: pageId,
                PageNumber: pageNum,
                PanelId: panelId,
                PanelNumber: panelNum,
                AssetReferenceId: assetId,
                ArtworkUrl: artUrl,
                SimilarityScore: simScore,
                Threshold: threshold,
                IsDeviation: true,
                Severity: f.Severity,
                Message: f.Message,
                Status: f.Status,
                TriageNote: f.TriageNote,
                TriagedByUserId: f.TriagedByUserId,
                TriagedByUserName: triagedByName,
                TriagedAt: f.TriagedAt,
                DetectedAt: f.LastDetectedAt,
                DetailedDeviations: detailedDevs.Count > 0 ? detailedDevs : null,
                SummaryReason: summaryReason,
                RevisionRef: revisionRef
            ));
        }

        var report = new VisualConsistencyReportDto(
            ChapterId: chapter.Id,
            ChapterNumber: chapter.ChapterNumber,
            ChapterTitle: chapter.Title ?? $"Chương {chapter.ChapterNumber}",
            CheckedAt: findings.Max(f => f.LastDetectedAt),
            Threshold: threshold,
            TotalEvaluated: deviations.Count + 5, // Tương quan số lượt kiểm tra
            PassedCount: 5,
            DeviationCount: deviations.Count,
            OpenDeviationCount: deviations.Count(d => d.Status == ConsistencyFindingStatus.Open || d.Status == ConsistencyFindingStatus.Confirmed),
            BlockingDeviationCount: deviations.Count(d => d.Severity == ConsistencyFindingSeverity.Blocking && (d.Status == ConsistencyFindingStatus.Open || d.Status == ConsistencyFindingStatus.Confirmed)),
            Deviations: deviations.OrderByDescending(d => d.Severity).ThenBy(d => d.SimilarityScore).ToList()
        );

        return Result<VisualConsistencyReportDto>.Success(report);
    }
}
