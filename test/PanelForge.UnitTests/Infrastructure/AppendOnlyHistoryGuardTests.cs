using FluentAssertions;
using PanelForge.Domain.Entities.Assets;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Infrastructure;

/// <summary>
/// Guard append-only trong SaveChangesAsync (BR-01, Module 4): chặn xoá và sửa cột nghiệp vụ trên lịch sử,
/// nhưng vẫn cho phép các thao tác hợp lệ (gửi duyệt, gán lại khung, xoá nháp Bible chưa có lịch sử duyệt).
/// </summary>
public class AppendOnlyHistoryGuardTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _chapterId = Guid.NewGuid();

    private ScriptVersion NewScriptVersion() =>
        ScriptVersion.Create(_chapterId, 1, "bản thử", _userId, "hash-1", "[]", 0);

    private AssetReference NewAssetReference(Guid? panelId = null) =>
        AssetReference.Create(Guid.NewGuid(), Guid.NewGuid(), _chapterId, Guid.NewGuid(), Guid.NewGuid(),
            1, null, "art.png", _userId, panelId);

    [Fact]
    public async Task ScriptVersion_ModifiedContentHash_IsRejected()
    {
        var version = NewScriptVersion();
        _db.ScriptVersions.Add(version);
        await _db.SaveChangesAsync();

        _db.Entry(version).Property("ContentHash").CurrentValue = "tampered";

        var act = () => _db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ScriptVersion.ContentHash*");
    }

    [Fact]
    public async Task ScriptVersion_Deleted_IsRejected()
    {
        var version = NewScriptVersion();
        _db.ScriptVersions.Add(version);
        await _db.SaveChangesAsync();

        _db.ScriptVersions.Remove(version);

        var act = () => _db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ScriptVersion*");
    }

    [Fact]
    public async Task ScriptVersion_MarkSubmitted_IsAllowed()
    {
        var version = NewScriptVersion();
        _db.ScriptVersions.Add(version);
        await _db.SaveChangesAsync();

        version.MarkSubmitted(_userId);
        await _db.SaveChangesAsync();

        version.SubmittedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AssetReference_ReassignPanel_IsAllowed()
    {
        var reference = NewAssetReference(panelId: Guid.NewGuid());
        _db.AssetReferences.Add(reference);
        await _db.SaveChangesAsync();

        reference.ReassignPanel(Guid.NewGuid());
        var act = () => _db.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AssetReference_ModifiedFileName_IsRejected()
    {
        var reference = NewAssetReference();
        _db.AssetReferences.Add(reference);
        await _db.SaveChangesAsync();

        _db.Entry(reference).Property("FileName").CurrentValue = "other.png";

        var act = () => _db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AssetReference.FileName*");
    }

    [Fact]
    public async Task ChapterSnapshot_Deleted_IsRejected()
    {
        var snapshot = ChapterSnapshot.Create(_chapterId, 1, 1L, _userId, "{}", "hash");
        _db.ChapterSnapshots.Add(snapshot);
        await _db.SaveChangesAsync();

        _db.ChapterSnapshots.Remove(snapshot);

        var act = () => _db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ChapterSnapshot*");
    }

    [Fact]
    public async Task AuditLog_ModifiedAction_IsRejected()
    {
        var log = AuditLog.Create("CREATE_X", "X");
        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync();

        _db.Entry(log).Property("Action").CurrentValue = "NOTHING";

        var act = () => _db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AuditLog.Action*");
    }

    [Fact]
    public async Task BibleRevisionEvent_DirectDelete_IsRejected()
    {
        var revision = NewRevision();
        _db.SeriesBibles.Add(revision.bible);
        await _db.SaveChangesAsync();

        var created = revision.rev.Events.Single();
        _db.BibleRevisionEvents.Remove(created);

        var act = () => _db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*BibleRevisionEvent*");
    }

    [Fact]
    public async Task BibleDraftWithoutReviewHistory_CanBeDeleted_WithItsDraftEvents()
    {
        var revision = NewRevision();
        _db.SeriesBibles.Add(revision.bible);
        await _db.SaveChangesAsync();

        _db.BibleEntryRevisions.Remove(revision.rev);

        var act = () => _db.SaveChangesAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task BibleRevisionWithReviewEvent_CannotBeDeleted_WithItsEvents()
    {
        var revision = NewRevision();
        revision.rev.SubmitConcept(Guid.NewGuid(), "/assets/concept-1.png", null);
        _db.SeriesBibles.Add(revision.bible);
        await _db.SaveChangesAsync();

        _db.BibleEntryRevisions.Remove(revision.rev);

        var act = () => _db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*BibleRevisionEvent*");
    }


    private (PanelForge.Domain.Entities.Bible.SeriesBible bible, BibleEntryRevision rev) NewRevision()
    {
        var bible = PanelForge.Domain.Entities.Bible.SeriesBible.Create(Guid.NewGuid(), Guid.NewGuid(), "Bible");
        var entry = bible.AddEntry(PanelForge.Domain.Enums.BibleEntryCategory.Character, "A_DUOC", "A Dược", "Mô tả");
        var rev = entry.AddRevision("Khởi tạo", "{}", "C9279D77", authorId: Guid.NewGuid(), isInitialVersion: true);
        return (bible, rev);
    }
}
