using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.Chapters.Queries.GetChapterCast;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Application.Features.ScriptLines.Models;
using PanelForge.Application.Features.ScriptVersions;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.Infrastructure.Services;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Chapters;

/// <summary>
/// Toàn bộ Unit Tests cho BIB-12: Recompute cast, SpeakerResolver, Refs payload, Panel cast revisions, Hash, GET /cast.
/// </summary>
public class ChapterCastRecomputeTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IWorkspaceAuthorizationService> _auth = new();
    private readonly Mock<IPageRepository> _pageRepo = new();
    private readonly Mock<IChapterCastInheritanceResolver> _mockInheritance = new();
    private readonly StudioWorkspace _studio;
    private readonly DomainSeries _series;
    private readonly Chapter _chapter;
    private readonly Scene _scene;
    private readonly SeriesBible _bible;
    private readonly ChapterCastInheritanceResolver _resolver;
    private readonly ChapterCastService _castService;
    private readonly Guid _writerId = Guid.NewGuid();
    private readonly Guid _producerId = Guid.NewGuid();

    public ChapterCastRecomputeTests()
    {
        _resolver = new ChapterCastInheritanceResolver(_db);
        _castService = new ChapterCastService(_db, _resolver);

        _studio = StudioWorkspace.Create("Test Studio", _producerId);
        _db.StudioWorkspaces.Add(_studio);
        _series = DomainSeries.Create(_studio.Id, "Manga Cast", ReadingDirection.RightToLeft);
        _db.Series.Add(_series);
        _chapter = Chapter.Create(_series.Id, 1, "Chương 1");
        _db.Chapters.Add(_chapter);
        _scene = Scene.Create(_chapter.Id, 1, "Cảnh 1");
        _db.Scenes.Add(_scene);

        _bible = SeriesBible.Create(_series.Id, _studio.Id, "Manga Bible");
        _db.SeriesBibles.Add(_bible);
        _db.SaveChanges();

        _auth.Setup(a => a.GetMemberRoleAsync(_writerId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Writer);
        _auth.Setup(a => a.GetMemberRoleAsync(_producerId, _studio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceRole.Producer);
    }

    [Fact]
    public async Task SpeakerResolver_MatchesAliasAndCaseInsensitive()
    {
        // 1. Khớp bí danh & tên có dấu viết hoa khác nhau
        var naruto = BibleEntry.Create(_bible.Id, BibleEntryCategory.Character, "CHR01", "Uzumaki Naruto", "Nhân vật chính");
        naruto.SetAliases(["Cửu Vĩ", "Naruto"]);
        _db.BibleEntries.Add(naruto);
        _db.BibleEntryNames.Add(BibleEntryName.Create(_bible.Id, "Uzumaki Naruto", naruto.Id, isPrimary: true));
        _db.BibleEntryNames.Add(BibleEntryName.Create(_bible.Id, "Cửu Vĩ", naruto.Id, isPrimary: false));
        _db.BibleEntryNames.Add(BibleEntryName.Create(_bible.Id, "Naruto", naruto.Id, isPrimary: false));

        var line1 = ScriptLine.Create(_scene.Id, 1, text: "Tôi sẽ trở thành Hokage!", kind: ScriptLineKind.Dialogue, speakerLabel: "cửu vĩ");
        var line2 = ScriptLine.Create(_scene.Id, 2, text: "Tin tôi đi!", kind: ScriptLineKind.Dialogue, speakerLabel: "NARUTO");
        _db.ScriptLines.AddRange(line1, line2);
        await _db.SaveChangesAsync();

        var service = new ChapterCastService(_db, _mockInheritance.Object);
        await service.RecomputeAsync(_chapter.Id, _writerId);
        await _db.SaveChangesAsync();

        var binding = await _db.ChapterBibleBindings.FirstOrDefaultAsync(b => b.ChapterId == _chapter.Id && b.BibleEntryId == naruto.Id);
        binding.Should().NotBeNull();
        binding!.Source.Should().Be(ChapterBibleBindingSource.Speaker);
    }

    [Fact]
    public async Task SpeakerResolver_StripsThoughtAndNghiSuffix_AndHandlesThoughtKind()
    {
        // 2 & 3. Minh (nghĩ) và dòng Thought
        var minh = BibleEntry.Create(_bible.Id, BibleEntryCategory.Character, "CHR02", "Minh", "Nhân vật Minh");
        _db.BibleEntries.Add(minh);
        _db.BibleEntryNames.Add(BibleEntryName.Create(_bible.Id, "Minh", minh.Id, isPrimary: true));

        var line1 = ScriptLine.Create(_scene.Id, 1, text: "Kẻ địch ở đâu?", kind: ScriptLineKind.Dialogue, speakerLabel: "Minh (nghĩ)");
        var line2 = ScriptLine.Create(_scene.Id, 2, text: "Có tiếng động lạ...", kind: ScriptLineKind.Thought, speakerLabel: "Minh (nghi)");
        var line3 = ScriptLine.Create(_scene.Id, 3, text: "Ta phải cẩn thận.", kind: ScriptLineKind.Thought, speakerLabel: "Minh");
        _db.ScriptLines.AddRange(line1, line2, line3);
        await _db.SaveChangesAsync();

        var service = new ChapterCastService(_db, _mockInheritance.Object);
        await service.RecomputeAsync(_chapter.Id, _writerId);
        await _db.SaveChangesAsync();

        var binding = await _db.ChapterBibleBindings.FirstOrDefaultAsync(b => b.ChapterId == _chapter.Id && b.BibleEntryId == minh.Id);
        binding.Should().NotBeNull();
        binding!.Source.Should().Be(ChapterBibleBindingSource.Speaker);
    }

    [Fact]
    public async Task SpeakerResolver_AutoCreatesDraftStubOnce_ForMultipleLinesWithSameName()
    {
        // 5. Tự tạo một lần cho nhiều dòng cùng tên
        var line1 = ScriptLine.Create(_scene.Id, 1, text: "Lôi Độn!", kind: ScriptLineKind.Dialogue, speakerLabel: "Kakashi");
        var line2 = ScriptLine.Create(_scene.Id, 2, text: "Nguy hiểm thật...", kind: ScriptLineKind.Dialogue, speakerLabel: "Kakashi (nghĩ)");
        var line3 = ScriptLine.Create(_scene.Id, 3, text: "Chidori!", kind: ScriptLineKind.Dialogue, speakerLabel: "KAKASHI");
        _db.ScriptLines.AddRange(line1, line2, line3);
        await _db.SaveChangesAsync();

        var service = new ChapterCastService(_db, _mockInheritance.Object);
        await service.RecomputeAsync(_chapter.Id, _writerId);
        await _db.SaveChangesAsync();

        var stubs = await _db.BibleEntries.Where(e => e.SeriesBibleId == _bible.Id && e.Name.ToLower() == "kakashi").ToListAsync();
        stubs.Should().HaveCount(1);
        var stub = stubs.Single();
        stub.IsStub.Should().BeTrue();
        stub.Category.Should().Be(BibleEntryCategory.Character);

        var binding = await _db.ChapterBibleBindings.FirstOrDefaultAsync(b => b.ChapterId == _chapter.Id && b.BibleEntryId == stub.Id);
        binding.Should().NotBeNull();
        binding!.Source.Should().Be(ChapterBibleBindingSource.Speaker);

        var audit = await _db.AuditLogs.FirstOrDefaultAsync(a => a.Action == "AutoCreateFromSpeaker" && a.EntityId == stub.Id.ToString());
        audit.Should().NotBeNull();
    }

    [Fact]
    public async Task RecomputeAsync_CleansUpOrphanStubs_WhenNoLongerReferenced()
    {
        // 6. Dọn mục thừa: IsStub, hệ thống tạo, không còn binding và tham chiếu nào, chỉ có sự kiện Created -> xoá
        var stub = BibleEntry.CreateStub(_bible.Id, BibleEntryCategory.Character, "Kẻ Qua Đường", description: "");
        _db.BibleEntries.Add(stub);
        _db.BibleEntryNames.Add(BibleEntryName.Create(_bible.Id, "Kẻ Qua Đường", stub.Id, isPrimary: true));

        var line = ScriptLine.Create(_scene.Id, 1, text: "Xin chào!", kind: ScriptLineKind.Dialogue, speakerLabel: "Kẻ Qua Đường");
        _db.ScriptLines.Add(line);
        await _db.SaveChangesAsync();

        var service = new ChapterCastService(_db, _mockInheritance.Object);
        await service.RecomputeAsync(_chapter.Id, _writerId);
        await _db.SaveChangesAsync();

        // Stub vẫn tồn tại vì đang được line nhắc tới
        _db.BibleEntries.Any(e => e.Id == stub.Id).Should().BeTrue();

        // Giờ đổi người nói của line sang người khác
        line.UpdateContent(text: "Xin chào!", dialogueText: "Xin chào!", stageDirection: null, kind: ScriptLineKind.Dialogue, speakerLabel: "Người Khác");
        await _db.SaveChangesAsync();

        await service.RecomputeAsync(_chapter.Id, _writerId);
        await _db.SaveChangesAsync();

        // Stub "Kẻ Qua Đường" phải bị dọn sạch
        _db.BibleEntries.Any(e => e.Id == stub.Id).Should().BeFalse();
        _db.BibleEntryNames.Any(n => n.BibleEntryId == stub.Id).Should().BeFalse();
    }

    [Fact]
    public async Task WriterChoiceChange_ChangesContentHash_WorkingCopyDiffersFromSubmitted()
    {
        // 11. Đổi bản Writer chọn sau khi gửi -> bản hiện tại ≠ bản đã gửi
        var entry = BibleEntry.Create(_bible.Id, BibleEntryCategory.Character, "CHR04", "Sakura", "Mô tả Sakura");
        var rev1 = entry.AddRevision("v1", "{}", "hash1");
        var rev2 = entry.AddRevision("v2", "{}", "hash2");
        _db.BibleEntries.Add(entry);
        _db.BibleEntryNames.Add(BibleEntryName.Create(_bible.Id, "Sakura", entry.Id, isPrimary: true));

        var line = ScriptLine.Create(_scene.Id, 1, text: "Chào mọi người!", kind: ScriptLineKind.Dialogue, speakerLabel: "Sakura");
        _db.ScriptLines.Add(line);
        await _db.SaveChangesAsync();

        // Commit ban đầu
        var version = await ScriptSnapshotBuilder.CommitAsync(_db, _chapter.Id, _writerId, "Bản 1", default, _castService);
        version.MarkSubmitted(_writerId);
        await _db.SaveChangesAsync();

        var submittedHash = version.ContentHash;

        // Writer chọn bản cụ thể cho chương: chosen_by_writer = true
        var binding = await _db.ChapterBibleBindings.SingleAsync(b => b.ChapterId == _chapter.Id && b.BibleEntryId == entry.Id);
        binding.UpdateBinding(rev2.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: true, _writerId);
        await _db.SaveChangesAsync();

        var (newRows, workingCopyHash, _) = await ScriptSnapshotBuilder.BuildAsync(_db, _chapter.Id, default);

        workingCopyHash.Should().NotBe(submittedHash);
    }

    [Fact]
    public async Task GetChapterCastQuery_EvaluatesSubmitAndStartGatesCorrectly()
    {
        // 14. GET /api/chapters/{id}/cast
        var stubEntry = BibleEntry.CreateStub(_bible.Id, BibleEntryCategory.Character, "Nhân Vật Ẩn");
        _db.BibleEntries.Add(stubEntry);
        _db.ChapterBibleBindings.Add(ChapterBibleBinding.Create(_chapter.Id, stubEntry.Id, null, ChapterBibleBindingSource.Speaker, isCrowdAtBind: false));

        var canonEntry = BibleEntry.Create(_bible.Id, BibleEntryCategory.Character, "CHR05", "Itachi", "Itachi");
        var canonRev = canonEntry.AddRevision("v1", "{}", "hash");
        canonRev.Lock(_producerId);
        _db.BibleEntries.Add(canonEntry);
        _db.ChapterBibleBindings.Add(ChapterBibleBinding.Create(_chapter.Id, canonEntry.Id, canonRev.Id, ChapterBibleBindingSource.Speaker, isCrowdAtBind: false));
        await _db.SaveChangesAsync();

        var castGate = new ChapterCastGate(_db, _castService, _resolver);
        var queryHandler = new GetChapterCastQueryHandler(_db, _auth.Object, castGate);
        var result = await queryHandler.Handle(new GetChapterCastQuery(_chapter.Id, _writerId), default);

        result.IsSuccess.Should().BeTrue();
        var cast = result.Value!;
        cast.Entities.Should().HaveCount(2);

        // Submit gate bị chặn vì stubEntry chưa định nghĩa và không phải Quần chúng
        cast.SubmitGate.CanSubmit.Should().BeFalse();
        cast.SubmitGate.Blockers.Should().Contain(b => b.BibleEntryId == stubEntry.Id);

        // Gắn cờ Quần chúng cho stubEntry -> submit gate qua
        var stubBinding = await _db.ChapterBibleBindings.SingleAsync(b => b.ChapterId == _chapter.Id && b.BibleEntryId == stubEntry.Id);
        stubBinding.SetCrowdAtBind(true);
        await _db.SaveChangesAsync();

        var result2 = await queryHandler.Handle(new GetChapterCastQuery(_chapter.Id, _writerId), default);
        result2.Value!.SubmitGate.CanSubmit.Should().BeTrue();
        result2.Value.Gate.CanStart.Should().BeTrue();
    }
}
