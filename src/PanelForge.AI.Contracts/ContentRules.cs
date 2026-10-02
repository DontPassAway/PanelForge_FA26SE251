namespace PanelForge.AI.Contracts;

/// <summary>
/// Quy tắc kiểm tra output của agent bằng code (không giao cho LLM). Hàm thuần, không I/O.
/// AI service dùng để kiểm trước khi báo step; Backend dùng lại để kiểm lần nữa khi nhận step và khi Accept.
/// Mỗi hàm trả danh sách lỗi (rỗng = hợp lệ).
/// </summary>
public static class ContentRules
{
    private const double Eps = 1e-9;
    public const int MaxPromptLength = 1500;
    public const int MaxReasonLength = 500;

    public static IReadOnlyList<string> ValidateScript(
        ScriptDraft draft, Guid runId, Guid sceneId, ISet<Guid> validSpeakerIds, ISet<Guid> bibleRevisionIds)
    {
        var errors = new List<string>();
        CheckHeader(errors, draft.SchemaVersion, draft.RunId, runId);
        if (draft.SceneId != sceneId) errors.Add("sceneId không thuộc phạm vi của run.");
        if (draft.Lines.Count == 0) errors.Add("Script phải có ít nhất một dòng.");

        var ids = new HashSet<Guid>();
        var orders = new HashSet<int>();
        foreach (var line in draft.Lines)
        {
            if (line.LineId == Guid.Empty || !ids.Add(line.LineId)) errors.Add($"lineId rỗng hoặc trùng ở dòng {line.Order}.");
            if (line.Order <= 0 || !orders.Add(line.Order)) errors.Add($"order không hợp lệ hoặc trùng: {line.Order}.");
            if (string.IsNullOrWhiteSpace(line.Dialogue) && string.IsNullOrWhiteSpace(line.StageDirection))
                errors.Add($"Dòng {line.Order} phải có dialogue hoặc stageDirection.");
            if (line.SpeakerId is { } sp && !validSpeakerIds.Contains(sp))
                errors.Add($"speakerId của dòng {line.Order} không thuộc Bible của Series.");
        }
        if (orders.Count > 0 && !orders.OrderBy(o => o).SequenceEqual(Enumerable.Range(1, orders.Count)))
            errors.Add("order của các dòng phải liên tục từ 1.");
        foreach (var id in draft.BibleRevisionIds)
            if (!bibleRevisionIds.Contains(id)) errors.Add($"bibleRevisionId {id} không nằm trong snapshot của run.");
        return errors;
    }

    /// <param name="bibleByRevision">revisionId → (entryId, văn bản Bible dùng làm căn cứ: tên + mô tả).</param>
    public static IReadOnlyList<string> ValidateContinuity(
        ContinuityReport report, Guid runId, ScriptDraft script,
        IReadOnlyDictionary<Guid, (Guid EntryId, string Text)> bibleByRevision)
    {
        var errors = new List<string>();
        CheckHeader(errors, report.SchemaVersion, report.RunId, runId);
        var lineIds = script.Lines.Select(l => l.LineId).ToHashSet();
        var findingIds = new HashSet<string>();

        foreach (var f in report.Findings)
        {
            var tag = $"finding '{f.FindingId}'";
            if (string.IsNullOrWhiteSpace(f.FindingId) || !findingIds.Add(f.FindingId)) errors.Add("findingId rỗng hoặc trùng.");
            if (f.Severity is not (AiProtocol.SeverityBlocking or AiProtocol.SeverityWarning or AiProtocol.SeverityInfo))
                errors.Add($"{tag}: severity không hợp lệ ('{f.Severity}').");
            if (string.IsNullOrWhiteSpace(f.Description)) errors.Add($"{tag}: thiếu mô tả.");
            if (f.ScriptLineIds.Count == 0) errors.Add($"{tag}: thiếu liên kết script line.");
            foreach (var id in f.ScriptLineIds)
                if (!lineIds.Contains(id)) errors.Add($"{tag}: scriptLineId {id} không tồn tại trong script đầu vào.");
            if (f.Evidence.Count == 0) errors.Add($"{tag}: thiếu bằng chứng từ Bible.");
            foreach (var e in f.Evidence)
            {
                if (!bibleByRevision.TryGetValue(e.BibleRevisionId, out var src) || src.EntryId != e.BibleEntryId)
                {
                    errors.Add($"{tag}: bibleRevisionId {e.BibleRevisionId} không thuộc snapshot của run.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(e.Quote) || !Normalize(src.Text).Contains(Normalize(e.Quote)))
                    errors.Add($"{tag}: trích dẫn không có trong Bible revision {e.BibleRevisionId}.");
            }
        }
        return errors;
    }

    public static IReadOnlyList<string> ValidateLayout(
        StoryboardLayout layout, Guid runId, Guid sceneId, ScriptDraft script, string readingDirection,
        ISet<Guid> validCharacterIds)
    {
        var errors = new List<string>();
        CheckHeader(errors, layout.SchemaVersion, layout.RunId, runId);
        if (layout.SceneId != sceneId) errors.Add("sceneId không thuộc phạm vi của run.");
        if (!string.Equals(layout.ReadingDirection, readingDirection, StringComparison.OrdinalIgnoreCase))
            errors.Add($"readingDirection phải là {readingDirection}.");
        if (layout.Pages.Count == 0) errors.Add("Layout phải có ít nhất một trang.");

        var lineIds = script.Lines.Select(l => l.LineId).ToHashSet();
        var covered = new HashSet<Guid>();
        var pageNumbers = layout.Pages.Select(p => p.PageNumber).ToList();
        if (!pageNumbers.OrderBy(n => n).SequenceEqual(Enumerable.Range(1, pageNumbers.Count)))
            errors.Add("pageNumber phải duy nhất và liên tục từ 1.");

        foreach (var page in layout.Pages)
        {
            if (page.Panels.Count == 0) errors.Add($"Trang {page.PageNumber} không có panel.");
            if (!page.Panels.Select(p => p.PanelNumber).OrderBy(n => n)
                    .SequenceEqual(Enumerable.Range(1, page.Panels.Count)))
                errors.Add($"panelNumber của trang {page.PageNumber} phải duy nhất và liên tục từ 1.");

            foreach (var p in page.Panels)
            {
                var tag = $"panel {page.PageNumber}.{p.PanelNumber}";
                errors.AddRange(ValidateBbox(p.Bbox, tag));
                if (p.CoordinateSpace != AiProtocol.PagePixelSpace)
                    errors.Add($"{tag}: coordinateSpace phải là '{AiProtocol.PagePixelSpace}'.");
                if (string.IsNullOrWhiteSpace(p.Camera)) errors.Add($"{tag}: thiếu góc máy.");
                if (string.IsNullOrWhiteSpace(p.Description)) errors.Add($"{tag}: thiếu mô tả.");
                if (p.SourceScriptLineIds.Count == 0) errors.Add($"{tag}: thiếu sourceScriptLineIds.");
                foreach (var id in p.SourceScriptLineIds)
                {
                    if (!lineIds.Contains(id)) errors.Add($"{tag}: sourceScriptLineId {id} không tồn tại trong script đầu vào.");
                    covered.Add(id);
                }
                foreach (var c in p.CharacterIds)
                    if (!validCharacterIds.Contains(c)) errors.Add($"{tag}: characterId {c} không thuộc Bible của Series.");
            }
        }

        var missing = lineIds.Count(id => !covered.Contains(id));
        if (missing > 0) errors.Add($"{missing} dòng script chưa được panel nào tham chiếu.");

        errors.AddRange(ValidateReadingOrder(layout, readingDirection));
        return errors;
    }

    public static IReadOnlyList<string> ValidateSupervisor(
        SupervisorDecision d, Guid runId, IReadOnlyList<string> allowed)
    {
        var errors = new List<string>();
        CheckHeader(errors, d.SchemaVersion, d.RunId, runId);
        if (!allowed.Contains(d.Action))
            errors.Add($"action '{d.Action}' không nằm trong danh sách được phép: {string.Join(", ", allowed)}.");
        if (string.IsNullOrWhiteSpace(d.Reason)) errors.Add("Thiếu reason.");
        if (d.Instruction is { Length: > 600 }) errors.Add("instruction tối đa 600 ký tự.");
        return errors;
    }

    public static IReadOnlyList<string> ValidateReview(ReviewReport report, Guid runId, ScriptDraft script)
    {
        var errors = new List<string>();
        CheckHeader(errors, report.SchemaVersion, report.RunId, runId);
        if (report.Verdict is not ("Approve" or "ApproveWithChanges" or "RequestChanges"))
            errors.Add($"verdict không hợp lệ ('{report.Verdict}').");
        if (string.IsNullOrWhiteSpace(report.Summary)) errors.Add("Thiếu summary.");
        var lineIds = script.Lines.Select(l => l.LineId).ToHashSet();
        foreach (var item in report.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Comment)) errors.Add($"Mục review '{item.Target}' thiếu nhận xét.");
            foreach (var id in item.ScriptLineIds)
                if (!lineIds.Contains(id)) errors.Add($"Mục review '{item.Target}': scriptLineId {id} không tồn tại.");
        }
        return errors;
    }

    /// <summary>
    /// Prompt ảnh của Artist: mỗi panel trong context có đúng một prompt; không có panel lạ; reference ID chỉ lấy từ
    /// <paramref name="validReferenceIds"/> (Bible hiệu lực tại chương); mọi nhân vật xuất hiện trong panel phải được tham chiếu.
    /// </summary>
    public static IReadOnlyList<string> ValidateArtist(
        ArtistPromptSet set, Guid runId, Guid sceneId, ArtistContext ctx, ISet<Guid> validReferenceIds)
    {
        var errors = new List<string>();
        CheckHeader(errors, set.SchemaVersion, set.RunId, runId);
        if (set.SceneId != sceneId) errors.Add("sceneId không thuộc phạm vi của run.");
        if (set.LayoutRevisionId != ctx.LayoutRevisionId) errors.Add("layoutRevisionId không khớp layout nền của run.");

        var expected = ctx.Panels.ToDictionary(p => p.PanelId);
        var seen = new HashSet<Guid>();
        foreach (var p in set.Panels)
        {
            if (!expected.TryGetValue(p.PanelId, out var panel))
            {
                errors.Add($"panelId {p.PanelId} không thuộc layout nền của run.");
                continue;
            }
            var tag = $"panel {panel.PageNumber}.{panel.PanelNumber}";
            if (!seen.Add(p.PanelId)) errors.Add($"{tag}: có hơn một prompt.");
            if (p.PageNumber != panel.PageNumber || p.PanelNumber != panel.PanelNumber)
                errors.Add($"{tag}: pageNumber/panelNumber không khớp context.");
            if (string.IsNullOrWhiteSpace(p.Prompt)) errors.Add($"{tag}: thiếu prompt.");
            else if (p.Prompt.Length > MaxPromptLength) errors.Add($"{tag}: prompt dài quá {MaxPromptLength} ký tự.");
            foreach (var r in p.ReferenceIds)
                if (!validReferenceIds.Contains(r)) errors.Add($"{tag}: referenceId {r} không thuộc Bible hiệu lực của run.");
            if (p.ReferenceIds.Distinct().Count() != p.ReferenceIds.Count) errors.Add($"{tag}: referenceId bị lặp.");
            foreach (var c in panel.CharacterIds)
                if (!p.ReferenceIds.Contains(c)) errors.Add($"{tag}: thiếu tham chiếu nhân vật {c} có trong panel.");
        }
        foreach (var panel in ctx.Panels.Where(p => !seen.Contains(p.PanelId)))
            errors.Add($"panel {panel.PageNumber}.{panel.PanelNumber}: chưa có prompt.");
        return errors;
    }

    /// <summary>
    /// Đề xuất rút gọn thoại: mỗi dòng tối đa MỘT đề xuất (single-binding), mỗi bubble tối đa một đề xuất; liên kết dòng↔bubble/panel,
    /// văn bản gốc và giới hạn phải khớp context do code cung cấp; bản đề xuất phải ngắn hơn bản gốc và nằm trong giới hạn ƯỚC LƯỢNG.
    /// Không có lệnh nào ở đây dựa vào nhận định "vừa bubble" của LLM.
    /// </summary>
    public static IReadOnlyList<string> ValidateLettering(
        LetteringSuggestionSet set, Guid runId, Guid sceneId, LetteringContext ctx)
    {
        var errors = new List<string>();
        CheckHeader(errors, set.SchemaVersion, set.RunId, runId);
        if (set.SceneId != sceneId) errors.Add("sceneId không thuộc phạm vi của run.");

        var lines = ctx.Lines.ToDictionary(l => l.LineId);
        var usedLines = new HashSet<Guid>();
        var usedElements = new HashSet<Guid>();
        foreach (var s in set.Suggestions)
        {
            if (!lines.TryGetValue(s.SourceScriptLineId, out var line))
            {
                errors.Add($"sourceScriptLineId {s.SourceScriptLineId} không thuộc script của scene.");
                continue;
            }
            var tag = $"dòng {line.Order}";
            if (!usedLines.Add(line.LineId)) errors.Add($"{tag}: có hơn một đề xuất cho cùng một dòng thoại.");
            if (s.TargetElementId != line.ElementId) errors.Add($"{tag}: targetElementId không khớp liên kết do code xác định.");
            if (s.TargetElementId is { } el && !usedElements.Add(el)) errors.Add($"{tag}: bubble {el} đã gắn với đề xuất khác (single-binding).");
            if (s.PanelId != line.PanelId) errors.Add($"{tag}: panelId không khớp liên kết do code xác định.");
            if (s.OriginalText != line.Dialogue) errors.Add($"{tag}: originalText không khớp thoại gốc.");
            if (s.MaxChars != line.MaxChars || s.LimitSource != line.LimitSource) errors.Add($"{tag}: giới hạn chữ không khớp số liệu do code cung cấp.");
            if (string.IsNullOrWhiteSpace(s.SuggestedText)) errors.Add($"{tag}: thiếu suggestedText.");
            else
            {
                if (s.SuggestedText.Length >= line.Dialogue.Length) errors.Add($"{tag}: suggestedText phải ngắn hơn thoại gốc.");
                if (s.SuggestedText.Length > line.MaxChars) errors.Add($"{tag}: suggestedText ({s.SuggestedText.Length}) vượt giới hạn ước lượng {line.MaxChars} ký tự.");
                if (s.WithinEstimatedLimit != s.SuggestedText.Length <= line.MaxChars) errors.Add($"{tag}: withinEstimatedLimit không khớp số liệu của code.");
            }
            if (string.IsNullOrWhiteSpace(s.Reason)) errors.Add($"{tag}: thiếu lý do.");
            else if (s.Reason.Length > MaxReasonLength) errors.Add($"{tag}: lý do dài quá {MaxReasonLength} ký tự.");
        }
        return errors;
    }

    /// <summary>
    /// Báo cáo tiến độ: mọi rủi ro/đề xuất phải trích khoá fact có trong snapshot do Backend tính; mọi con số % trong văn bản
    /// phải là số do code tính (LLM không tự tính phần trăm); DataGaps phải đúng như snapshot (thiếu dữ liệu phải nói rõ).
    /// </summary>
    public static IReadOnlyList<string> ValidateProduction(ProductionReport report, Guid runId, ProgressContext ctx)
    {
        var errors = new List<string>();
        CheckHeader(errors, report.SchemaVersion, report.RunId, runId);
        if (string.IsNullOrWhiteSpace(report.Headline)) errors.Add("Thiếu headline.");
        if (string.IsNullOrWhiteSpace(report.Summary)) errors.Add("Thiếu summary.");

        var keys = ctx.Facts.Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowedPercents = ctx.AllowedPercentages.Select(NormalizePercent).ToHashSet();

        void CheckSources(string tag, IReadOnlyList<string> sources, params string?[] texts)
        {
            if (sources.Count == 0) errors.Add($"{tag}: thiếu nguồn (sourceFactKeys).");
            foreach (var k in sources)
                if (!keys.Contains(k)) errors.Add($"{tag}: nguồn '{k}' không có trong snapshot tiến độ.");
            foreach (var t in texts)
                foreach (var pct in PercentsIn(t))
                    if (!allowedPercents.Contains(pct)) errors.Add($"{tag}: con số {pct} không phải số liệu do hệ thống tính.");
        }

        foreach (var r in report.Risks)
        {
            if (string.IsNullOrWhiteSpace(r.Description)) errors.Add("Một rủi ro thiếu mô tả.");
            if (r.Severity is not (AiProtocol.SeverityBlocking or AiProtocol.SeverityWarning or AiProtocol.SeverityInfo))
                errors.Add($"Rủi ro '{Short(r.Description)}': severity không hợp lệ ('{r.Severity}').");
            CheckSources($"Rủi ro '{Short(r.Description)}'", r.SourceFactKeys, r.Description);
        }
        foreach (var rec in report.Recommendations)
        {
            if (string.IsNullOrWhiteSpace(rec.Action)) errors.Add("Một đề xuất xử lý thiếu nội dung.");
            CheckSources($"Đề xuất '{Short(rec.Action)}'", rec.SourceFactKeys, rec.Action, rec.Rationale);
        }
        foreach (var t in new[] { report.Headline, report.Summary })
            foreach (var pct in PercentsIn(t))
                if (!allowedPercents.Contains(pct)) errors.Add($"Báo cáo: con số {pct} không phải số liệu do hệ thống tính.");

        if (!report.DataGaps.SequenceEqual(ctx.DataGaps)) errors.Add("dataGaps phải đúng như snapshot tiến độ.");
        if (report.Facts.Count != ctx.Facts.Count) errors.Add("facts phải đúng như snapshot tiến độ.");
        return errors;
    }

    private static string Short(string? s) => s is null ? "" : s.Length <= 40 ? s : s[..40] + "…";

    private static readonly System.Text.RegularExpressions.Regex PercentRx =
        new(@"(\d+(?:[.,]\d+)?)\s*%", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Chuẩn hoá "40 %", "40,0%" → "40%".</summary>
    public static string NormalizePercent(string s)
    {
        var m = PercentRx.Match(s);
        var num = (m.Success ? m.Groups[1].Value : s.Trim().TrimEnd('%')).Replace(',', '.');
        if (num.Contains('.')) num = num.TrimEnd('0').TrimEnd('.');
        return num + "%";
    }

    private static IEnumerable<string> PercentsIn(string? text) =>
        string.IsNullOrEmpty(text) ? [] : PercentRx.Matches(text).Select(m => NormalizePercent(m.Value));

    /// <summary>Bbox theo page-normalized: hữu hạn, w/h dương, trong 0..1, x+w ≤ 1, y+h ≤ 1.</summary>
    public static IReadOnlyList<string> ValidateBbox(Bbox b, string tag)
    {
        var errors = new List<string>();
        double[] v = [b.X, b.Y, b.W, b.H];
        if (v.Any(d => !double.IsFinite(d))) { errors.Add($"{tag}: bbox chứa giá trị không hữu hạn."); return errors; }
        if (b.W <= 0 || b.H <= 0) errors.Add($"{tag}: bbox phải có w,h > 0.");
        if (b.X < 0 || b.Y < 0 || b.X > 1 || b.Y > 1 || b.W > 1 || b.H > 1) errors.Add($"{tag}: bbox phải nằm trong 0..1.");
        if (b.X + b.W > 1 + Eps) errors.Add($"{tag}: x + w vượt quá 1.");
        if (b.Y + b.H > 1 + Eps) errors.Add($"{tag}: y + h vượt quá 1.");
        return errors;
    }

    /// <summary>
    /// readingOrder duy nhất, liên tục từ 1 trên toàn scene; trang tăng dần; trong cùng trang thứ tự phải khớp hướng đọc:
    /// cùng hàng (tâm y lệch nhỏ hơn nửa chiều cao panel thấp hơn) → LTR tâm x tăng, RTL tâm x giảm; khác hàng → hàng dưới đọc sau.
    /// </summary>
    public static IReadOnlyList<string> ValidateReadingOrder(StoryboardLayout layout, string readingDirection)
    {
        var errors = new List<string>();
        var all = layout.Pages.SelectMany(pg => pg.Panels.Select(pn => (Page: pg.PageNumber, Panel: pn))).ToList();
        var orders = all.Select(x => x.Panel.ReadingOrder).OrderBy(o => o).ToList();
        if (!orders.SequenceEqual(Enumerable.Range(1, all.Count)))
        {
            errors.Add("readingOrder phải duy nhất và liên tục từ 1 trên toàn scene.");
            return errors;
        }

        var seq = all.OrderBy(x => x.Panel.ReadingOrder).ToList();
        var rtl = string.Equals(readingDirection, "RightToLeft", StringComparison.OrdinalIgnoreCase);
        var vertical = string.Equals(readingDirection, "Vertical", StringComparison.OrdinalIgnoreCase);

        for (var i = 1; i < seq.Count; i++)
        {
            var (pa, a) = seq[i - 1];
            var (pb, b) = seq[i];
            if (pb < pa) { errors.Add($"readingOrder {b.ReadingOrder}: trang quay ngược."); continue; }
            if (pb != pa) continue;

            var (acx, acy) = (a.Bbox.X + a.Bbox.W / 2, a.Bbox.Y + a.Bbox.H / 2);
            var (bcx, bcy) = (b.Bbox.X + b.Bbox.W / 2, b.Bbox.Y + b.Bbox.H / 2);
            var sameRow = !vertical && Math.Abs(acy - bcy) < Math.Min(a.Bbox.H, b.Bbox.H) / 2;
            var ok = sameRow ? (rtl ? bcx < acx : bcx > acx) : bcy > acy;
            if (!ok)
                errors.Add($"readingOrder sai hướng {readingDirection}: panel {pb}.{b.PanelNumber} đọc sau panel {pa}.{a.PanelNumber}.");
        }
        return errors;
    }

    private static void CheckHeader(List<string> errors, int schemaVersion, Guid payloadRunId, Guid runId)
    {
        if (schemaVersion != AiProtocol.SchemaVersion) errors.Add($"schemaVersion phải là {AiProtocol.SchemaVersion}.");
        if (payloadRunId != runId) errors.Add("runId không khớp với run hiện tại.");
    }

    internal static string Normalize(string s) => string.Join(' ', s.ToLowerInvariant()
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
