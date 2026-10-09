using System.Text.RegularExpressions;
using PanelForge.AI.Contracts;

namespace PanelForge.AI.Demo.Fixtures;

/// <summary>
/// Fixture xác định cho kịch bản demo "Thành phố mưa" (cần Bible mẫu do AiDemoSeeder tạo: nhân vật AN, BA_LAN...).
/// - Writer lượt 0: An dùng "chìa khóa bạc" ở Chương 3 (sai so với Bible). Lượt sửa (round ≥ 1): sửa lại cho khớp Bible.
///   Brief chứa "[mock:stubborn]" → Writer không bao giờ sửa (để demo dừng ở WaitingForHuman sau 2 lượt).
/// - Continuity: quy tắc cố định — dòng của An nhắc "chìa khóa bạc" là mâu thuẫn Blocking, trích dẫn nguyên văn Bible.
/// - Storyboard: chia 2 dòng/panel, bố cục theo hướng đọc. Editor: báo cáo tóm tắt từ số liệu trong prompt.
/// Dữ liệu chỉ là mẫu, không chứa thông tin thật.
/// </summary>
public static class MockFixtures
{
    public const string StubbornMarker = "[mock:stubborn]";

    public static string Respond(string userMessage)
    {
        var agent = Regex.Match(userMessage, @"\[\[agent:(\w+)\]\]").Groups[1].Value;
        return agent switch
        {
            "supervisor" => Supervisor(userMessage),
            "writer" => Writer(userMessage),
            "continuity" => Continuity(userMessage),
            "storyboard" => Storyboard(userMessage),
            "editor" => Editor(userMessage),
            "artist" => Artist(userMessage),
            "letterer" => Letterer(userMessage),
            "producer" => Producer(userMessage),
            _ => throw new InvalidOperationException("Mock: không nhận ra agent trong prompt.")
        };
    }

    /// <summary>Brief chứa marker này → Supervisor (Mock) leo thang sớm sang con người thay vì gọi Writer sửa tiếp.</summary>
    public const string EscalateMarker = "[mock:escalate]";

    private static string Json(object o) => AiJson.Serialize(o);

    // ── Supervisor (điều phối) ───────────────────────────────────────────────
    // Chính sách xác định, chọn trong danh sách [[allowed:...]] do code cung cấp:
    // sau Continuity có finding chặn: còn lượt → Writer (hoặc StopForHuman nếu brief có EscalateMarker); còn lại → hành động hợp lệ đầu tiên.
    private static string Supervisor(string msg)
    {
        var allowed = Regex.Match(msg, @"\[\[allowed:([^\]]+)\]\]").Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var blocking = int.Parse(Regex.Match(msg, @"\[\[blocking:(\d+)\]\]").Groups[1].Value);
        var workflow = Regex.Match(msg, @"\[\[workflow:(\w+)\]\]").Groups[1].Value;

        var action = allowed[0];
        if (allowed.Contains("StopForHuman") && allowed.Contains("Writer"))
            action = msg.Contains(EscalateMarker, StringComparison.OrdinalIgnoreCase) ? "StopForHuman" : "Writer";

        var (reason, instruction) = action switch
        {
            "Writer" when blocking > 0 => ("Continuity báo mâu thuẫn chặn nhưng còn lượt sửa: gọi Writer sửa lại thay vì dừng.",
                (string?)"Bỏ chi tiết mâu thuẫn với Bible theo finding Blocking; giữ nguyên nhịp cảnh và các dòng không liên quan."),
            "Writer" => ("Chưa có script cho Scene: gọi Writer viết bản đầu theo brief.",
                "Bám sát Series Bible tại chương hiện tại; không dùng vật phẩm hay sự kiện mà Bible chưa cho phép."),
            "Continuity" => ("Vừa có bản script mới: kiểm tra continuity với Bible trước khi chia panel.", null),
            "Storyboard" => ("Continuity đã sạch: chuyển sang Storyboard chia Page/Panel.",
                "Thứ tự đọc phải đúng hướng đọc của Series; mỗi dòng script phải có panel tham chiếu."),
            "Editor" => ("Đã đủ đầu vào: gọi Editor viết báo cáo review cho người biên tập.",
                "Nêu rõ nhịp thoại và các điểm người biên tập cần chú ý khi quyết định."),
            "Artist" => ("Layout đã duyệt có panel: gọi Artist soạn prompt ảnh cho từng panel.",
                "Giữ nhất quán ngoại hình nhân vật theo Bible; luôn tham chiếu nhân vật có trong panel."),
            "Letterer" => ("Thoại đã gắn với panel: gọi Letterer đề xuất rút gọn cho vừa bubble.",
                "Giữ ý và giọng nhân vật; chỉ rút gọn phần cần thiết."),
            "Producer" => ("Có snapshot tiến độ do Backend tính: gọi Producer viết báo cáo.",
                "Chỉ dùng số liệu trong fact; nêu rõ dữ liệu còn thiếu."),
            "StopForHuman" when workflow == "Storyboard" => ("Continuity báo finding chặn trên script có sẵn; workflow này không có Writer để sửa: dừng để con người quyết định.", null),
            "StopForHuman" when allowed.Length == 1 => ("Đã hết lượt sửa mà còn finding chặn: dừng để con người quyết định.", null),
            "StopForHuman" => ("Mâu thuẫn có vẻ nằm ở chính brief nên Writer sửa tiếp khó hiệu quả: leo thang sang con người.", null),
            _ => (workflow switch
            {
                "Script" => "Script đã qua Continuity sạch: kết thúc, đề xuất script chờ con người Accept hoặc Reject.",
                "Artist" => "Đã có prompt cho mọi panel: kết thúc, prompt ảnh chờ người duyệt.",
                "Letterer" => "Đã xem xét mọi dòng thoại: kết thúc, đề xuất rút gọn (nếu có) chờ người duyệt.",
                "Producer" => "Báo cáo tiến độ đã xong: kết thúc, báo cáo chỉ để tham khảo.",
                "Editor" => "Báo cáo review đã xong: kết thúc, báo cáo chỉ để tham khảo.",
                _ => "Editor đã review xong: kết thúc workflow, các đề xuất chờ con người Accept hoặc Reject."
            }, null)
        };
        return Json(new { action, reason, instruction });
    }

    // ── Writer ───────────────────────────────────────────────────────────────
    private static string Writer(string msg)
    {
        var round = int.Parse(Regex.Match(msg, @"\[\[round:(\d+)\]\]").Groups[1].Value);
        var fixedUp = round >= 1 && !msg.Contains(StubbornMarker, StringComparison.OrdinalIgnoreCase);

        object line4 = fixedUp
            ? new { speaker = "AN", dialogue = "Bà ơi, hay để cháu thử cạy ổ khóa này bằng chiếc kẹp tóc?",
                    stageDirection = "An cúi xuống, dùng chiếc kẹp tóc nhỏ thử cạy ổ khóa chiếc tủ cũ; trong tay không có chìa khóa nào." }
            : new { speaker = "AN", dialogue = "Cháu có chìa khóa bạc đây, bà thử xem có khớp ổ khóa không.",
                    stageDirection = "An lấy chiếc chìa khóa bạc từ túi áo, tra vào ổ khóa chiếc tủ cũ." };
        object line5 = fixedUp
            ? new { speaker = "BA_LAN", dialogue = "Khoan đã… chưa đến lúc. Rồi sẽ có ngày bà trao cho cháu thứ cháu cần.", stageDirection = (string?)null }
            : new { speaker = "BA_LAN", dialogue = "Lạ thật… tiếng mưa như đang đếm ngược.", stageDirection = (string?)null };

        return Json(new
        {
            lines = new object[]
            {
                new { speaker = (string?)null, dialogue = (string?)null,
                      stageDirection = "Nửa đêm, mưa vẫn gõ đều trên mái tiệm đồng hồ. An quét bụi giữa những chiếc đồng hồ ngừng chạy." },
                new { speaker = "AN", dialogue = "Đếm đến trăm tiếng mưa nữa là mình đóng cửa.", stageDirection = (string?)null },
                new { speaker = "BA_LAN", dialogue = "An ơi, ra giúp bà một chút.", stageDirection = "Bà Lan đứng ngoài hiên, mắt dán vào ổ khóa chiếc tủ cũ." },
                line4,
                line5
            },
            revisionNotes = fixedUp
                ? "Bỏ việc An dùng chìa khóa bạc (Bible: An chỉ nhận được chìa khóa bạc từ Chương 5); đổi sang thử cạy ổ khóa bằng kẹp tóc."
                : (string?)null
        });
    }

    // ── Continuity ───────────────────────────────────────────────────────────
    private static readonly Regex LineRx =
        new(@"\[(L\d+)\] speaker=(\S+) \| dialogue=""(.*?)"" \| stage=""(.*?)""", RegexOptions.Compiled);

    private static string Continuity(string msg)
    {
        var anKey = Regex.Match(msg, @"\[(B\d+)\] code=AN \|").Groups[1].Value;
        var plotKey = Regex.Match(msg, @"\[(B\d+)\] code=MOC_CK5 \|").Groups[1].Value;

        var flagged = LineRx.Matches(msg)
            .Where(m =>
            {
                var text = (m.Groups[3].Value + " " + m.Groups[4].Value).ToLowerInvariant();
                var anActs = m.Groups[2].Value == "AN" || m.Groups[4].Value.Contains("An ", StringComparison.Ordinal);
                return anActs && text.Contains("chìa khóa bạc");
            })
            .Select(m => m.Groups[1].Value)
            .ToList();

        if (flagged.Count == 0 || anKey.Length == 0)
            return Json(new { findings = Array.Empty<object>(), summary = "Script nhất quán với Series Bible tại chương này." });

        var evidence = new List<object>
        {
            new { bibleKey = anKey, quote = "An chỉ nhận được chìa khóa bạc từ Chương 5" }
        };
        if (plotKey.Length > 0)
            evidence.Add(new { bibleKey = plotKey, quote = "Chìa khóa bạc chỉ chuyển sang An ở Chương 5" });

        return Json(new
        {
            findings = new object[]
            {
                new
                {
                    severity = "Blocking",
                    category = "ItemTimeline",
                    description = "An dùng chìa khóa bạc ở Chương 3, nhưng Bible ghi An chỉ nhận được chìa khóa bạc từ Chương 5.",
                    lineKeys = flagged,
                    evidence,
                    suggestedFix = "Bỏ việc An cầm/dùng chìa khóa bạc; đổi sang hành động khác không cần chiếc chìa khóa (ví dụ thử cạy ổ khóa)."
                }
            },
            summary = "Phát hiện 1 mâu thuẫn chặn: An sở hữu chìa khóa bạc trước Chương 5."
        });
    }

    // ── Storyboard ───────────────────────────────────────────────────────────
    private record Slot(double X, double Y, double W, double H);

    private static string Storyboard(string msg)
    {
        var rtl = msg.Contains("[[direction:RightToLeft]]");
        var lines = Regex.Matches(msg, @"\[(L\d+)\] speaker=(\S+)")
            .Select(m => (Key: m.Groups[1].Value, Speaker: m.Groups[2].Value)).ToList();

        // Mỗi trang tối đa 4 ô theo thứ tự đọc: ô rộng trên → 2 ô giữa (đọc theo hướng) → ô rộng dưới
        Slot[] template = rtl
            ? [new(0, 0, 1, 0.34), new(0.5, 0.34, 0.5, 0.33), new(0, 0.34, 0.5, 0.33), new(0, 0.67, 1, 0.33)]
            : [new(0, 0, 1, 0.34), new(0, 0.34, 0.5, 0.33), new(0.5, 0.34, 0.5, 0.33), new(0, 0.67, 1, 0.33)];
        string[] cameras = ["wide establishing shot", "medium shot", "close-up", "over-the-shoulder shot"];

        var chunks = lines.Chunk(2).ToList();
        var pages = chunks.Chunk(template.Length).Select(pageChunks => new
        {
            panels = pageChunks.Select((chunk, i) =>
            {
                var slot = template[i];
                return new
                {
                    bbox = new { x = slot.X, y = slot.Y, w = slot.W, h = slot.H },
                    camera = cameras[i % cameras.Length],
                    description = $"Khung cảnh thể hiện các dòng {string.Join(", ", chunk.Select(c => c.Key))} trong tiệm đồng hồ ngày mưa.",
                    characters = chunk.Select(c => c.Speaker).Where(s => s != "-").Distinct().ToList(),
                    lineKeys = chunk.Select(c => c.Key).ToList()
                };
            }).ToList()
        }).ToList();

        return Json(new { pages, notes = "Layout mẫu (Mock): 2 dòng/panel, thứ tự đọc theo " + (rtl ? "RTL" : "LTR") + "." });
    }

    // ── Artist ───────────────────────────────────────────────────────────────
    // Mỗi panel một prompt; tham chiếu Bible của MỌI nhân vật có trong panel (validator của code bắt buộc), thêm địa điểm đầu tiên nếu có.
    private static string Artist(string msg)
    {
        var bibleByCode = Regex.Matches(msg, @"\[(B\d+)\] code=(\S+) \| category=(\w+)")
            .ToDictionary(m => m.Groups[2].Value, m => (Key: m.Groups[1].Value, Category: m.Groups[3].Value));
        var locationKey = bibleByCode.Values.FirstOrDefault(v => v.Category == "Location").Key;

        var panels = new List<object>();
        var lines = msg.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var m = Regex.Match(lines[i], @"^\[(P\d+)\] page=(\d+) panel=(\d+) order=\d+ camera=(.*?) \| characters=(\S*)");
            if (!m.Success) continue;
            var description = i + 1 < lines.Length ? lines[i + 1].Replace("description:", "").Trim() : "";
            var codes = m.Groups[5].Value is "-" or "" ? [] : m.Groups[5].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var refs = codes.Where(bibleByCode.ContainsKey).Select(c => bibleByCode[c].Key).ToList();
            if (locationKey is { Length: > 0 }) refs.Add(locationKey);
            panels.Add(new
            {
                panelKey = m.Groups[1].Value,
                prompt = $"Manga panel, {m.Groups[4].Value.Trim()}. {description} Black-and-white ink, screentone shading, rainy night atmosphere, clean line art.",
                referenceKeys = refs.Distinct().ToList(),
                consistencyNotes = codes.Length > 0
                    ? $"Giữ nhất quán ngoại hình {string.Join(", ", codes)} theo Bible hiệu lực tại chương (Mock)."
                    : "Panel không có nhân vật: chú ý giữ nhất quán bối cảnh với panel trước (Mock)."
            });
        }
        return Json(new { panels, styleSummary = "Mực đen trắng, tông xám screentone, không khí đêm mưa (Mock)." });
    }

    // ── Letterer ─────────────────────────────────────────────────────────────
    // Rút gọn các dòng thoại dài (≥ 40 ký tự): cắt ở ranh giới từ rồi thêm "…" — chỉ là fixture, không phải xử lý ngữ nghĩa thật.
    private static string Letterer(string msg)
    {
        var suggestions = new List<object>();
        foreach (Match m in Regex.Matches(msg,
                     @"\[(L\d+)\] speaker=(\S+) \| page=\S+ panel=\S+ \| bubble=.*? \| limit=(\d+) \(.*?\) \| length=(\d+)[^|]*\| dialogue=""(.*)""\s*$",
                     RegexOptions.Multiline))
        {
            var (limit, length, text) = (int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value), m.Groups[5].Value);
            if (length < 40) continue;
            var target = Math.Min(limit, (int)(length * 0.7)) - 1;
            var cut = text[..Math.Min(text.Length, Math.Max(1, target))];
            var space = cut.LastIndexOf(' ');
            if (space > 8) cut = cut[..space];
            var suggested = cut.TrimEnd(',', ' ', '.') + "…";
            suggestions.Add(new
            {
                lineKey = m.Groups[1].Value,
                suggestedText = suggested,
                reason = length > limit
                    ? "Thoại vượt giới hạn ước lượng: lược phần cuối câu, giữ ý chính và giọng nhân vật (Mock)."
                    : "Làm gọn phần cuối câu cho thoáng bubble, vẫn giữ ý chính và giọng nhân vật (Mock)."
            });
        }
        return Json(new
        {
            suggestions,
            summary = suggestions.Count == 0 ? "Không dòng thoại nào cần rút gọn." : $"Đề xuất rút gọn {suggestions.Count} dòng thoại (Mock)."
        });
    }

    // ── Producer ─────────────────────────────────────────────────────────────
    // Chỉ diễn giải fact do code cung cấp: rủi ro = fact quá hạn / bị chặn / cần sửa; luôn trích khoá fact; không tự tính số.
    private static string Producer(string msg)
    {
        var facts = Regex.Matches(msg, @"^\[(F\d+)\] (.+)$", RegexOptions.Multiline)
            .Select(m => (Key: m.Groups[1].Value, Text: m.Groups[2].Value.Trim())).ToList();
        var gaps = int.Parse(Regex.Match(msg, @"\[\[gaps:(\d+)\]\]").Groups[1].Value);

        var risks = new List<object>();
        var recs = new List<object>();
        foreach (var f in facts)
        {
            if (f.Text.Contains("đã quá", StringComparison.OrdinalIgnoreCase) && f.Text.StartsWith("Assignment", StringComparison.Ordinal))
            {
                risks.Add(new { severity = "Warning", description = $"Có assignment đã quá hạn ({f.Text})", sourceFactKeys = new[] { f.Key } });
                recs.Add(new { action = "Làm việc với người phụ trách để xác nhận lại deadline hoặc gỡ vướng mắc.", rationale = "Assignment đã quá hạn theo số liệu hệ thống.", sourceFactKeys = new[] { f.Key }, suggestedOwnerRole = "Producer" });
            }
            else if (f.Text.Contains("ReworkRequired") && f.Text.StartsWith("Assignment ", StringComparison.Ordinal)
                     || f.Text.Contains("Blocked") && f.Text.StartsWith("Assignment ", StringComparison.Ordinal))
            {
                risks.Add(new { severity = "Warning", description = $"Có assignment đang bị chặn hoặc cần làm lại ({f.Text})", sourceFactKeys = new[] { f.Key } });
            }
            else if (f.Text.Contains("sắp đến hạn", StringComparison.OrdinalIgnoreCase))
            {
                risks.Add(new { severity = "Info", description = $"Có assignment sắp đến hạn ({f.Text})", sourceFactKeys = new[] { f.Key } });
            }
        }
        if (recs.Count == 0 && facts.Count > 0)
            recs.Add(new { action = gaps > 0 ? "Bổ sung dữ liệu còn thiếu (assignment, deadline, ngày phát hành) để lần báo cáo sau đánh giá được đầy đủ." : "Tiếp tục theo dõi tiến độ theo các fact hiện có.",
                           rationale = "Dựa trên snapshot tiến độ hiện tại.", sourceFactKeys = new[] { facts[0].Key }, suggestedOwnerRole = "Producer" });

        var completion = facts.FirstOrDefault(f => f.Text.StartsWith("Hoàn thành assignment", StringComparison.Ordinal)).Text;
        var summary = (completion is null ? "Chưa có số liệu hoàn thành assignment." : completion)
                      + (gaps > 0 ? " Một số phần chưa đánh giá được vì thiếu dữ liệu (xem mục dữ liệu còn thiếu)." : "")
                      + $" {risks.Count} rủi ro có căn cứ từ fact.";
        return Json(new
        {
            headline = risks.Count > 0 ? "Có rủi ro tiến độ cần xử lý" : gaps > 0 ? "Chưa đủ dữ liệu để đánh giá đầy đủ" : "Tiến độ chưa thấy rủi ro đáng kể",
            summary,
            risks,
            recommendations = recs
        });
    }

    // ── Editor ───────────────────────────────────────────────────────────────
    private static string Editor(string msg)
    {
        int N(string name) => int.Parse(Regex.Match(msg, $@"\[\[{name}:(\d+)\]\]").Groups[1].Value);
        var (round, lines, panels, pages, warnings) = (N("final-round"), N("lines"), N("panels"), N("pages"), N("warnings"));
        var firstKey = Regex.Match(msg, @"\[(L\d+)\]").Groups[1].Value;

        var items = new List<object>
        {
            new { target = "Scene", severity = "Info",
                  comment = round > 0
                      ? $"Script đã qua {round} lượt sửa của Writer để khớp Series Bible; Continuity lần cuối không còn finding chặn."
                      : "Script khớp Series Bible ngay lượt đầu.",
                  lineKeys = Array.Empty<string>() },
            new { target = "Page:1", severity = "Warning",
                  comment = "Nên kiểm tra nhịp thoại ở panel đầu: cảnh mở màn nên để khoảng lặng trước lời thoại đầu tiên.",
                  lineKeys = firstKey.Length > 0 ? new[] { firstKey } : Array.Empty<string>() }
        };

        return Json(new
        {
            verdict = warnings > 0 ? "ApproveWithChanges" : "Approve",
            summary = $"Đề xuất script {lines} dòng, bố cục {pages} trang / {panels} panel, mọi dòng đều có panel tham chiếu. " +
                      "Đây là đề xuất chờ người biên tập quyết định Accept hoặc Reject.",
            items,
            draftRejectComment = "Nếu Reject: nêu rõ dòng hoặc panel cần sửa và lý do (ví dụ nhịp thoại ở trang 1)."
        });
    }
}