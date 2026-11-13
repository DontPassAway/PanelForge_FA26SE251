using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Commands.RunThumbnail;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Entities.Assets;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Commands.ReviewSuggestion;

/// <summary>
/// Quyết định của người đối với đề xuất AI (BR-02). Accept = validate lại, kiểm Chapter Approved/Locked (BR-12) và
/// version (BR-11 → 409), rồi tạo content revision + decision + đổi trạng thái trong MỘT transaction (BR-01, BR-10, BR-18).
/// Accept KHÔNG đổi production stage hay Approve Chapter.
/// </summary>
public sealed record ReviewSuggestionCommand(
    Guid SeriesId,
    Guid SuggestionId,
    Guid UserId,
    AiSuggestionStatus Decision,
    string? Reason,
    long? ChapterVersion) : IRequest<Result<ReviewSuggestionResponse>>;

public sealed class ReviewSuggestionCommandHandler : IRequestHandler<ReviewSuggestionCommand, Result<ReviewSuggestionResponse>>
{
    private const int PageWidthPx = 1200, PageHeightPx = 1800; // mặc định của CreatePageCommand

    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPageRepository _pages;

    public ReviewSuggestionCommandHandler(
        IPanelForgeDbContext db, IWorkspaceAuthorizationService auth, IUnitOfWork unitOfWork, IPageRepository pages)
    {
        _db = db;
        _auth = auth;
        _unitOfWork = unitOfWork;
        _pages = pages;
    }

    public Task<Result<ReviewSuggestionResponse>> Handle(ReviewSuggestionCommand cmd, CancellationToken ct)
        // Marten (Page events) + EF cùng một transaction khi Accept layout; Script/Reject chỉ cần một SaveChanges nhưng dùng chung đường này.
        => _unitOfWork.ExecuteInTransactionAsync(token => HandleAsync(cmd, token), ct);

    private async Task<Result<ReviewSuggestionResponse>> HandleAsync(ReviewSuggestionCommand cmd, CancellationToken ct)
    {
        if (cmd.Decision is AiSuggestionStatus.Pending)
            return AiRunHelpers.Fail<ReviewSuggestionResponse>("Quyết định không hợp lệ.", ResultErrorCodes.Validation);

        var suggestion = await _db.AiSuggestions.FirstOrDefaultAsync(s => s.Id == cmd.SuggestionId && s.SeriesId == cmd.SeriesId, ct);
        if (suggestion is null)
            return AiRunHelpers.Fail<ReviewSuggestionResponse>("Đề xuất không tồn tại trong Series.", ResultErrorCodes.NotFound);
        // Quyền duyệt theo LOẠI đề xuất (script/layout/prompt ảnh/thoại), không phải một danh sách chung
        var reviewRoles = AiWorkflowPolicy.ReviewRoles(suggestion.Kind);
        if (!await _auth.HasWorkspaceRoleAsync(cmd.UserId, suggestion.WorkspaceId, reviewRoles, ct))
            return AiRunHelpers.Fail<ReviewSuggestionResponse>(
                $"Chỉ {string.Join(", ", reviewRoles)} của Workspace mới được quyết định đề xuất {suggestion.Kind}.", ResultErrorCodes.Forbidden);
        if (suggestion.Status != AiSuggestionStatus.Pending)
            return AiRunHelpers.Fail<ReviewSuggestionResponse>(
                $"Đề xuất đã được quyết định ({suggestion.Status}).", ResultErrorCodes.Conflict);

        var chapter = await _db.Chapters.FirstOrDefaultAsync(c => c.Id == suggestion.ChapterId, ct);
        if (chapter is null)
            return AiRunHelpers.Fail<ReviewSuggestionResponse>("Chapter của đề xuất không còn tồn tại.", ResultErrorCodes.NotFound);

        if (cmd.Decision == AiSuggestionStatus.ChangesRequested && string.IsNullOrWhiteSpace(cmd.Reason))
            return AiRunHelpers.Fail<ReviewSuggestionResponse>("RequestChanges phải kèm lý do.", ResultErrorCodes.Validation);

        var versionSeen = chapter.VersionVector; // trước khi Accept tăng version
        Guid? revisionId = null;
        if (cmd.Decision == AiSuggestionStatus.Accepted)
        {
            var applied = await ApplyAsync(cmd, suggestion, chapter, ct);
            if (applied.Error is not null) return AiRunHelpers.Fail<ReviewSuggestionResponse>(applied.Error, applied.Code);
            revisionId = applied.RevisionId;
        }

        suggestion.Decide(cmd.Decision);
        var decision = AiSuggestionDecision.Create(
            suggestion.Id, cmd.UserId, cmd.Decision, cmd.Reason, versionSeen, revisionId,
            appliesOutsideContent: suggestion.Kind is AiSuggestionKind.SeriesMetadata or AiSuggestionKind.Thumbnail);
        _db.AiSuggestionDecisions.Add(decision);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Chapter.VersionVector là concurrency token: có người sửa nội dung giữa chừng (BR-11).
            return AiRunHelpers.Fail<ReviewSuggestionResponse>(
                "Nội dung Chapter vừa bị thay đổi bởi người khác. Tải lại rồi quyết định lại.", ResultErrorCodes.Conflict);
        }

        return Result<ReviewSuggestionResponse>.Success(new ReviewSuggestionResponse(
            suggestion.Id, suggestion.Status, decision.Id, revisionId, chapter.VersionVector));
    }

    private sealed record Applied(Guid? RevisionId, string? Error, string? Code)
    {
        public static Applied Ok(Guid id) => new(id, null, null);
        public static Applied Fail(string error, string code) => new(null, error, code);
    }

    private async Task<Applied> ApplyAsync(ReviewSuggestionCommand cmd, AiSuggestion s, Chapter chapter, CancellationToken ct)
    {
        // Metadata cấp Series: không phụ thuộc trạng thái/version của Chapter, không tăng versionVector
        if (s.Kind == AiSuggestionKind.SeriesMetadata) return await ApplySeriesMetadataAsync(s, ct);
        // Ảnh panel: không phải nội dung có version (giống UploadArtwork), Accept nhiều thumbnail không làm stale lẫn nhau
        if (s.Kind == AiSuggestionKind.Thumbnail) return await ApplyThumbnailAsync(cmd, s, chapter, ct);

        // BR-12: chương Approved/Locked/Published chỉ đọc
        if (!chapter.IsContentEditable)
            return Applied.Fail($"Chapter đang ở trạng thái {chapter.Status} (chỉ đọc), không áp dụng được đề xuất.", ResultErrorCodes.Conflict);
        if (s.HasBlockingFindings)
            return Applied.Fail("Đề xuất còn finding chặn từ Continuity: chỉ được Reject hoặc RequestChanges.", ResultErrorCodes.Validation);
        if (cmd.ChapterVersion is null)
            return Applied.Fail("chapterVersion là bắt buộc khi Accept.", ResultErrorCodes.Validation);

        // BR-11: version người duyệt thấy, version nền của đề xuất và version hiện tại phải trùng nhau.
        // Layout dựa trên script đã Accept → nền là version ngay sau lần Accept script đó (mỗi Accept tăng Chapter.VersionVector).
        var expectedBase = s.BaseChapterVersion;
        if (s.Kind == AiSuggestionKind.Layout && s.ParentSuggestionId is not null)
        {
            var parentDecision = await _db.AiSuggestionDecisions.AsNoTracking().FirstOrDefaultAsync(
                d => d.SuggestionId == s.ParentSuggestionId && d.Decision == AiSuggestionStatus.Accepted, ct);
            if (parentDecision is null)
                return Applied.Fail("Cần Accept đề xuất script trước khi áp dụng layout.", ResultErrorCodes.Conflict);
            expectedBase = parentDecision.ChapterVersionAtDecision + 1;
        }
        if (cmd.ChapterVersion != chapter.VersionVector)
            return Applied.Fail(
                $"Version cũ: bạn gửi chapterVersion {cmd.ChapterVersion} nhưng Chapter hiện ở {chapter.VersionVector}. Tải lại rồi quyết định lại.",
                ResultErrorCodes.Conflict);
        if (expectedBase != chapter.VersionVector)
            return Applied.Fail(
                $"Version cũ: đề xuất dựa trên chapterVersion {expectedBase} nhưng Chapter hiện ở {chapter.VersionVector} (nội dung đã đổi sau khi AI chạy).",
                ResultErrorCodes.Conflict);

        var latestScript = await _db.ContentRevisions
            .Where(r => r.SceneId == s.SceneId && r.Kind == ContentRevisionKind.Script)
            .OrderByDescending(r => r.VersionNumber).FirstOrDefaultAsync(ct);

        var run = await _db.AiWorkflowRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == s.RunId, ct);
        var snapshot = run is null ? null : AiRunHelpers.ReadSnapshot(run);
        if (run is null || snapshot is null)
            return Applied.Fail("Không đọc được run/snapshot nguồn của đề xuất (thiếu provenance).", ResultErrorCodes.Validation);

        // BR-03: Scene vẫn thuộc đúng Chapter/Series
        var scene = await _db.Scenes.FirstOrDefaultAsync(x => x.Id == s.SceneId && x.ChapterId == chapter.Id, ct);
        if (scene is null || chapter.SeriesId != s.SeriesId)
            return Applied.Fail("Scene không còn thuộc Chapter/Series của đề xuất.", ResultErrorCodes.Validation);

        var result = s.Kind switch
        {
            AiSuggestionKind.Script => await ApplyScriptAsync(cmd, s, run, snapshot, scene, latestScript, ct),
            AiSuggestionKind.Layout => await ApplyLayoutAsync(cmd, s, run, snapshot, scene, chapter, latestScript, ct),
            AiSuggestionKind.ImagePrompt => await ApplyImagePromptAsync(s, run, snapshot, ct),
            AiSuggestionKind.LineRewrite => await ApplyLineRewriteAsync(s, latestScript, ct),
            _ => Applied.Fail($"Loại đề xuất {s.Kind} không áp dụng được.", ResultErrorCodes.Validation)
        };

        if (result.Error is null) chapter.IncrementVersion();
        return result;
    }

    private async Task<Applied> ApplyScriptAsync(
        ReviewSuggestionCommand cmd, AiSuggestion s, AiWorkflowRun run, RunSnapshot snapshot, Scene scene,
        ContentRevision? latest, CancellationToken ct)
    {
        if (latest?.Id != s.BaseScriptRevisionId)
            return Applied.Fail("Script của Scene đã có revision mới hơn lúc AI chạy.", ResultErrorCodes.Conflict);

        var draft = AiContentValidator.TryParse<ScriptDraft>(s.PayloadJson, out var parseError);
        if (draft is null) return Applied.Fail($"Payload script không hợp lệ: {parseError}", ResultErrorCodes.Validation);
        var errors = AiContentValidator.ValidateScript(run, snapshot, draft);
        if (errors.Count > 0) return Applied.Fail(AiContentValidator.Join(errors), ResultErrorCodes.Validation);

        var existing = await _db.ScriptLines.Where(l => l.SceneId == s.SceneId).OrderBy(l => l.LineOrder).ToListAsync(ct);

        // Chưa có revision nào nhưng Scene đang có dòng → chụp baseline để không mất lịch sử (BR-01)
        var previousId = latest?.Id;
        var nextVersion = (latest?.VersionNumber ?? 0) + 1;
        if (latest is null && existing.Count > 0)
        {
            var baselinePayload = AiJson.Serialize(existing.Select(l => new BaselineScriptLine(
                l.Id, l.LineOrder, l.SpeakerCharacterId, l.DialogueText, l.StageDirection)));
            var baseline = ContentRevision.Create(
                s.ChapterId, s.SceneId, ContentRevisionKind.Script, 1, "Baseline: script có sẵn trước khi áp dụng đề xuất AI",
                baselinePayload, AiContentValidator.Sha256(baselinePayload), null, null);
            _db.ContentRevisions.Add(baseline);
            previousId = baseline.Id;
            nextVersion = 2;
        }

        foreach (var line in existing) _db.ScriptLines.Remove(line); // soft-delete; nội dung cũ còn trong revision/baseline

        foreach (var line in draft.Lines.OrderBy(l => l.Order))
        {
            // Id do ứng dụng cấp ở draft → Storyboard.sourceScriptLineIds trỏ đúng dòng này
            _db.ScriptLines.Add(ScriptLine.Create(
                s.SceneId, line.Order, line.Dialogue, line.StageDirection, line.SpeakerId, line.LineId));
        }

        var revision = ContentRevision.Create(
            s.ChapterId, s.SceneId, ContentRevisionKind.Script, nextVersion,
            $"Script từ đề xuất AI {s.Id} (run {s.RunId})", s.PayloadJson, s.PayloadHash, previousId, s.Id,
            AiJson.Serialize(new { lineIds = draft.Lines.OrderBy(l => l.Order).Select(l => l.LineId) }));
        _db.ContentRevisions.Add(revision);
        return Applied.Ok(revision.Id);
    }

    /// <summary>
    /// Áp dụng prompt ảnh = ghi một ContentRevision(ImagePrompt) append-only (provenance + mapping panel → prompt → reference).
    /// Không sinh ảnh, không đụng Panel/Page/Bible. Chặn khi layout nền đã đổi hoặc panel không còn tồn tại.
    /// </summary>
    private async Task<Applied> ApplyImagePromptAsync(AiSuggestion s, AiWorkflowRun run, RunSnapshot snapshot, CancellationToken ct)
    {
        var set = AiContentValidator.TryParse<ArtistPromptSet>(s.PayloadJson, out var pe);
        if (set is null) return Applied.Fail($"Payload prompt không hợp lệ: {pe}", ResultErrorCodes.Validation);
        var errors = AiContentValidator.ValidateArtist(run, snapshot, set);
        if (errors.Count > 0) return Applied.Fail(AiContentValidator.Join(errors), ResultErrorCodes.Validation);

        var latestLayout = await AiWorkflowFacts.LatestLayoutAsync(_db, s.SceneId, ct);
        if (latestLayout?.Id != set.LayoutRevisionId)
            return Applied.Fail("Layout của Scene đã có revision mới hơn lúc AI soạn prompt.", ResultErrorCodes.Conflict);

        var panelIds = set.Panels.Select(p => p.PanelId).ToList();
        var alive = await _db.Panels.CountAsync(p => panelIds.Contains(p.Id), ct);
        if (alive != panelIds.Count)
            return Applied.Fail("Có panel của đề xuất không còn tồn tại (liên kết sai).", ResultErrorCodes.Conflict);

        var latest = await _db.ContentRevisions
            .Where(r => r.SceneId == s.SceneId && r.Kind == ContentRevisionKind.ImagePrompt)
            .OrderByDescending(r => r.VersionNumber).FirstOrDefaultAsync(ct);

        // Run chỉ soạn vài panel (panelIds): giữ mô tả đã duyệt của các panel còn lại trên cùng layout,
        // vì Thumbnail đọc revision ImagePrompt mới nhất của Scene
        var (payload, hash) = (s.PayloadJson, s.PayloadHash);
        var previous = latest is null ? null : AiContentValidator.TryParse<ArtistPromptSet>(latest.PayloadJson, out _);
        if (previous?.LayoutRevisionId == set.LayoutRevisionId)
        {
            var updated = set.Panels.Select(p => p.PanelId).ToHashSet();
            var kept = previous.Panels.Where(p => !updated.Contains(p.PanelId)).ToList();
            if (kept.Count > 0)
            {
                set = set with { Panels = kept.Concat(set.Panels).OrderBy(p => p.PageNumber).ThenBy(p => p.PanelNumber).ToList() };
                payload = AiJson.Serialize(set);
                hash = AiContentValidator.Sha256(payload);
            }
        }

        var revision = ContentRevision.Create(
            s.ChapterId, s.SceneId, ContentRevisionKind.ImagePrompt, (latest?.VersionNumber ?? 0) + 1,
            $"Prompt ảnh từ đề xuất AI {s.Id} (run {s.RunId})", payload, hash, latest?.Id, s.Id,
            AiJson.Serialize(new
            {
                layoutRevisionId = set.LayoutRevisionId,
                panels = set.Panels.Select(p => new { p.PanelId, p.PageNumber, p.PanelNumber, p.ReferenceIds })
            }));
        _db.ContentRevisions.Add(revision);
        return Applied.Ok(revision.Id);
    }

    /// <summary>
    /// Publisher Copilot: ghi synopsis + genre vào Series. 409 nếu Series đã bị sửa sau lúc AI chạy (BR-11: không ghi đè thay đổi của người khác).
    /// Logline/teaser/ageRating không có cột lưu, chỉ nằm trong payload để Publisher dùng.
    /// </summary>
    private async Task<Applied> ApplySeriesMetadataAsync(AiSuggestion s, CancellationToken ct)
    {
        var sug = AiContentValidator.TryParse<SeriesMetadataSuggestion>(s.PayloadJson, out var pe);
        if (sug is null) return Applied.Fail($"Payload metadata không hợp lệ: {pe}", ResultErrorCodes.Validation);
        if (CopilotRules.ValidateMetadata(sug.Metadata) is { Count: > 0 } errors)
            return Applied.Fail(string.Join(" ", errors), ResultErrorCodes.Validation);

        var series = await _db.Series.FirstOrDefaultAsync(x => x.Id == s.SeriesId, ct);
        if (series is null) return Applied.Fail("Series của đề xuất không còn tồn tại.", ResultErrorCodes.NotFound);
        if ((series.Synopsis ?? "").Trim() != (sug.OriginalSynopsis ?? "").Trim() || series.Genre != sug.OriginalGenre)
            return Applied.Fail("Synopsis/thể loại của Series đã được sửa sau khi AI đề xuất. Tải lại rồi chạy lại gợi ý.", ResultErrorCodes.Conflict);

        series.UpdateDetails(series.Title, sug.Metadata.Synopsis, series.ReadingDirection, sug.Metadata.Genre);
        return new Applied(null, null, null);
    }

    /// <summary>
    /// Thumbnail AI: tạo AssetReference phiên bản MỚI cho (Page, Panel, Stage); artwork cũ giữ nguyên (BR-01). Chặn khi Chapter chỉ đọc (BR-12),
    /// panel không còn, prompt ảnh đã có revision mới hơn lúc sinh, hoặc Artist duyệt không còn giữ việc ở stage đó.
    /// </summary>
    private async Task<Applied> ApplyThumbnailAsync(ReviewSuggestionCommand cmd, AiSuggestion s, Chapter chapter, CancellationToken ct)
    {
        if (!chapter.IsContentEditable)
            return Applied.Fail($"Chapter đang ở trạng thái {chapter.Status} (chỉ đọc), không áp dụng được đề xuất.", ResultErrorCodes.Conflict);
        var sug = AiContentValidator.TryParse<ThumbnailSuggestion>(s.PayloadJson, out var pe);
        if (sug is null) return Applied.Fail($"Payload thumbnail không hợp lệ: {pe}", ResultErrorCodes.Validation);

        var panel = await _db.Panels.AsNoTracking().FirstOrDefaultAsync(p => p.Id == sug.PanelId && p.PageId == sug.PageId, ct);
        if (panel is null || !await _db.Pages.AsNoTracking().AnyAsync(p => p.Id == sug.PageId && p.ChapterId == chapter.Id, ct))
            return Applied.Fail("Panel của thumbnail không còn tồn tại trong Chapter.", ResultErrorCodes.Conflict);
        var latestPrompt = await _db.ContentRevisions.AsNoTracking()
            .Where(r => r.SceneId == panel.SceneId && r.Kind == ContentRevisionKind.ImagePrompt)
            .OrderByDescending(r => r.VersionNumber).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        if (latestPrompt != sug.ImagePromptRevisionId)
            return Applied.Fail("Prompt ảnh của panel đã đổi sau khi sinh thumbnail. Sinh lại ảnh.", ResultErrorCodes.Conflict);
        var asset = await _db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == sug.AssetId && a.WorkspaceId == s.WorkspaceId, ct);
        if (asset is null || asset.Sha256 != sug.Sha256)
            return Applied.Fail("Ảnh của đề xuất không còn trong kho.", ResultErrorCodes.Conflict);

        // Producer nghiệm thu được; Artist phải đang giữ việc InProgress ở đúng stage của ảnh (cùng luật với tải artwork)
        if (!await _auth.HasWorkspaceRoleAsync(cmd.UserId, s.WorkspaceId, [WorkspaceRole.Producer], ct)
            && await ThumbnailRules.HeldStageAsync(_db, cmd.UserId, chapter.Id, sug.PageId, sug.PanelId, sug.StageId, ct) is null)
            return Applied.Fail("Bạn không còn giữ việc (InProgress) cho panel này ở stage của ảnh.", ResultErrorCodes.Forbidden);

        var versions = await _db.AssetReferences.AsNoTracking()
            .Where(r => r.PageId == sug.PageId && r.PanelId == sug.PanelId && r.PipelineStageId == sug.StageId)
            .Select(r => r.VersionNumber).ToListAsync(ct);
        var reference = AssetReference.Create(asset.Id, s.WorkspaceId, chapter.Id, sug.PageId, sug.StageId,
            versions.Count == 0 ? 1 : versions.Max() + 1, null, $"ai-thumbnail-{s.Id:N}", cmd.UserId, sug.PanelId);
        reference.CreatedBy = cmd.UserId.ToString();
        _db.AssetReferences.Add(reference);
        return new Applied(null, null, null);
    }

    /// <summary>
    /// Copilot (một dòng): revision Script MỚI (append-only). Chặn khi script đã có revision mới hơn hoặc dòng không còn khớp văn bản gốc;
    /// kiểm lại output bằng <see cref="CopilotRules"/> (ví dụ rút gọn vẫn phải ≤ maxChars).
    /// </summary>
    private async Task<Applied> ApplyLineRewriteAsync(AiSuggestion s, ContentRevision? latestScript, CancellationToken ct)
    {
        var sug = AiContentValidator.TryParse<LineRewriteSuggestion>(s.PayloadJson, out var pe);
        if (sug is null) return Applied.Fail($"Payload Copilot không hợp lệ: {pe}", ResultErrorCodes.Validation);
        if (CopilotRules.Validate(sug.Task, sug.OriginalText, sug.SuggestedText, sug.MaxChars) is { Count: > 0 } errors)
            return Applied.Fail(string.Join(" ", errors), ResultErrorCodes.Validation);
        if (latestScript?.Id != s.BaseScriptRevisionId)
            return Applied.Fail("Script của Scene đã có revision mới hơn lúc AI chạy.", ResultErrorCodes.Conflict);

        var existing = await _db.ScriptLines.Where(l => l.SceneId == s.SceneId).OrderBy(l => l.LineOrder).ToListAsync(ct);
        var line = existing.FirstOrDefault(l => l.Id == sug.SourceScriptLineId);
        if (line is null || line.DialogueText?.Trim() != sug.OriginalText)
            return Applied.Fail("Dòng thoại đã bị sửa hoặc xoá sau khi AI đề xuất.", ResultErrorCodes.Conflict);

        var (previousId, nextVersion) = CaptureBaseline(s, latestScript, existing);
        line.UpdateContent(sug.SuggestedText, line.StageDirection, line.SpeakerCharacterId);

        var payload = AiJson.Serialize(new ScriptDraft(
            AiProtocol.SchemaVersion, s.RunId, s.SceneId, 0,
            existing.Select(l => new ScriptDraftLine(l.Id, l.LineOrder, l.SpeakerCharacterId, l.DialogueText, l.StageDirection)).ToList(),
            AiRunHelpers.ReadGuidList(s.BibleRevisionIdsJson), $"Copilot {sug.Task}: sửa 1 dòng thoại."));
        var revision = ContentRevision.Create(
            s.ChapterId, s.SceneId, ContentRevisionKind.Script, nextVersion,
            $"Copilot {sug.Task} từ đề xuất AI {s.Id}", payload, AiContentValidator.Sha256(payload), previousId, s.Id,
            AiJson.Serialize(new
            {
                source = "Copilot",
                task = sug.Task,
                lineIds = existing.Select(l => l.Id),
                changes = new[] { new { sug.SourceScriptLineId, sug.OriginalText, sug.SuggestedText, sug.MaxChars } }
            }));
        _db.ContentRevisions.Add(revision);
        return Applied.Ok(revision.Id);
    }

    /// <summary>Chưa có revision nào nhưng Scene đang có dòng → chụp baseline để không mất lịch sử (BR-01).</summary>
    private (Guid? PreviousId, int NextVersion) CaptureBaseline(AiSuggestion s, ContentRevision? latest, List<ScriptLine> existing)
    {
        if (latest is not null || existing.Count == 0) return (latest?.Id, (latest?.VersionNumber ?? 0) + 1);
        var baselinePayload = AiJson.Serialize(existing.Select(l => new BaselineScriptLine(
            l.Id, l.LineOrder, l.SpeakerCharacterId, l.DialogueText, l.StageDirection)));
        var baseline = ContentRevision.Create(
            s.ChapterId, s.SceneId, ContentRevisionKind.Script, 1, "Baseline: script có sẵn trước khi áp dụng đề xuất AI",
            baselinePayload, AiContentValidator.Sha256(baselinePayload), null, null);
        _db.ContentRevisions.Add(baseline);
        return (baseline.Id, 2);
    }

    private async Task<Applied> ApplyLayoutAsync(
        ReviewSuggestionCommand cmd, AiSuggestion s, AiWorkflowRun run, RunSnapshot snapshot, Scene scene,
        Chapter chapter, ContentRevision? latestScript, CancellationToken ct)
    {
        string? se = null;
        ScriptDraft? script;
        if (s.ParentSuggestionId is { } pid)
        {
            // Layout từ workflow FullScene trỏ vào script của đề xuất cha: script đó phải đã được áp dụng
            var parent = await _db.AiSuggestions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pid, ct);
            if (parent is null || parent.Status != AiSuggestionStatus.Accepted)
                return Applied.Fail("Cần Accept đề xuất script trước khi áp dụng layout.", ResultErrorCodes.Conflict);
            script = AiContentValidator.TryParse<ScriptDraft>(parent.PayloadJson, out se);
        }
        else
        {
            // Layout từ workflow Storyboard dựa trên script có sẵn của Scene lúc chạy: script đó không được đổi kể từ đó
            if (latestScript?.Id != s.BaseScriptRevisionId)
                return Applied.Fail("Script của Scene đã có revision mới hơn lúc AI chạy.", ResultErrorCodes.Conflict);
            script = AiContentValidator.BaselineDraft(run, snapshot);
        }
        var layout = AiContentValidator.TryParse<StoryboardLayout>(s.PayloadJson, out var le);
        if (script is null || layout is null)
            return Applied.Fail($"Payload không hợp lệ: {se ?? le}", ResultErrorCodes.Validation);
        var errors = AiContentValidator.ValidateLayout(run, snapshot, layout, script);
        if (errors.Count > 0) return Applied.Fail(AiContentValidator.Join(errors), ResultErrorCodes.Validation);

        // Các ScriptLine mà layout trỏ tới phải thật sự tồn tại trong Scene (BR-03)
        var lineIds = script.Lines.Select(l => l.LineId).ToList();
        var liveLines = await _db.ScriptLines.Where(l => l.SceneId == s.SceneId && lineIds.Contains(l.Id)).ToListAsync(ct);
        if (liveLines.Count != lineIds.Count)
            return Applied.Fail("Script của Scene không còn khớp script đã Accept (đã bị sửa).", ResultErrorCodes.Conflict);

        // V1.2: Scene không chứa Page. Không ghi đè/xoá Panel có sẵn của Scene: layout AI chỉ thêm vào Scene chưa có Panel
        if (await _db.Panels.AnyAsync(p => p.SceneId == s.SceneId, ct))
            return Applied.Fail("Scene đã có Panel; xoá hoặc gỡ gán thủ công trước khi áp dụng layout AI.", ResultErrorCodes.Conflict);

        var maxPage = await _db.Pages.Where(p => p.ChapterId == chapter.Id).MaxAsync(p => (int?)p.PageNumber, ct) ?? 0;
        var mapping = new List<object>();
        foreach (var lp in layout.Pages.OrderBy(p => p.PageNumber))
        {
            var page = Page.CreateNew(chapter.Id, maxPage + lp.PageNumber, LayoutFormat.StandardPage, PageWidthPx, PageHeightPx, 300);
            await _pages.SaveAsync(page, ct);
            _db.Pages.Add(page);

            // Panel.ReadingOrder trong DB là thứ tự trong từng Page (xem CreatePanelCommand) → xếp hạng lại theo readingOrder toàn scene
            var ranked = lp.Panels.OrderBy(p => p.ReadingOrder).Select((p, i) => (Panel: p, Rank: i + 1)).ToList();
            var panelMap = new List<object>();
            foreach (var (lpPanel, rank) in ranked)
            {
                // Dữ liệu Panel có sẵn dùng toạ độ pixel {x,y,width,height} trên canvas của Page → ghi dạng đó để tương thích,
                // kèm bản chuẩn hoá page-normalized (0..1) mà AI sinh ra làm nguồn sự thật.
                var b = lpPanel.Bbox;
                var bbox = AiJson.Serialize(new
                {
                    x = (int)Math.Round(b.X * PageWidthPx), y = (int)Math.Round(b.Y * PageHeightPx),
                    width = (int)Math.Round(b.W * PageWidthPx), height = (int)Math.Round(b.H * PageHeightPx),
                    unit = "px", pageWidthPx = PageWidthPx, pageHeightPx = PageHeightPx,
                    normalized = new { x = b.X, y = b.Y, w = b.W, h = b.H, coordinateSpace = lpPanel.CoordinateSpace }
                });
                var panel = Panel.Create(page.Id, lpPanel.PanelNumber, rank, bbox, s.SceneId);
                _db.Panels.Add(panel);
                // Neo ScriptLine vào Panel (tối đa một Panel mỗi dòng: dòng xuất hiện ở nhiều panel thì giữ panel đầu theo thứ tự đọc)
                foreach (var line in liveLines.Where(l => l.PanelId is null && lpPanel.SourceScriptLineIds.Contains(l.Id)))
                    line.AssignToPanel(panel.Id);
                panelMap.Add(new
                {
                    panelNumber = lpPanel.PanelNumber, panelId = panel.Id, camera = lpPanel.Camera,
                    sourceScriptLineIds = lpPanel.SourceScriptLineIds
                });
            }
            mapping.Add(new { layoutPageNumber = lp.PageNumber, pageNumber = page.PageNumber, pageId = page.Id, panels = panelMap });
        }

        var latest = await _db.ContentRevisions
            .Where(r => r.SceneId == s.SceneId && r.Kind == ContentRevisionKind.Layout)
            .OrderByDescending(r => r.VersionNumber).FirstOrDefaultAsync(ct);
        var scriptRevision = await _db.ContentRevisions.AsNoTracking()
            .Where(r => r.SceneId == s.SceneId && r.Kind == ContentRevisionKind.Script)
            .OrderByDescending(r => r.VersionNumber).Select(r => r.Id).FirstOrDefaultAsync(ct);

        var revision = ContentRevision.Create(
            s.ChapterId, s.SceneId, ContentRevisionKind.Layout, (latest?.VersionNumber ?? 0) + 1,
            $"Layout từ đề xuất AI {s.Id} (run {s.RunId})", s.PayloadJson, s.PayloadHash, latest?.Id, s.Id,
            AiJson.Serialize(new { scriptRevisionId = scriptRevision, pages = mapping }));
        _db.ContentRevisions.Add(revision);
        return Applied.Ok(revision.Id);
    }
}