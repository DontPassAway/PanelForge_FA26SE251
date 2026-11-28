using FluentAssertions;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Domain;

/// <summary>
/// EventHash của BibleRevisionEvent (BR-01): băm đúng thời điểm được lưu, kiểm lại được, và UpdateDraft
/// không sửa nháp đã có lịch sử duyệt.
/// </summary>
public class BibleRevisionEventHashTests
{
    private static (SeriesBible bible, BibleEntryRevision rev) NewRevision()
    {
        var bible = SeriesBible.Create(Guid.NewGuid(), Guid.NewGuid(), "Bible");
        var entry = bible.AddEntry(BibleEntryCategory.Character, "A_DUOC", "A Dược", "Mô tả");
        var rev = entry.AddRevision("Khởi tạo", "{}", "C9279D77", authorId: Guid.NewGuid(), isInitialVersion: true);
        return (bible, rev);
    }

    [Fact]
    public async Task FreshEvent_HasValidHash_AfterSave_WithMicrosecondTimestamp()
    {
        var db = InMemoryDb.Create();
        var (bible, rev) = NewRevision();
        db.SeriesBibles.Add(bible);
        await db.SaveChangesAsync();

        var ev = rev.Events.Single();

        ev.HasValidHash().Should().BeTrue();
        (ev.CreatedAt.Ticks % 10).Should().Be(0, "Postgres timestamptz chỉ giữ micro-giây");
    }

    [Fact]
    public async Task TamperedHashedField_HasValidHash_IsFalse()
    {
        var db = InMemoryDb.Create();
        var (bible, rev) = NewRevision();
        db.SeriesBibles.Add(bible);
        await db.SaveChangesAsync();
        var ev = rev.Events.Single();
        ev.HasValidHash().Should().BeTrue();

        db.Entry(ev).Property("Comment").CurrentValue = "bị sửa tay";

        ev.HasValidHash().Should().BeFalse();
    }

    [Fact]
    public void UpdateDraft_AfterConceptSubmitted_Throws()
    {
        var (_, rev) = NewRevision();
        rev.SubmitConcept(Guid.NewGuid(), "/assets/concept-1.png", null);

        var act = () => rev.UpdateDraft("Nội dung mới", "{}", "HASH2", Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void UpdateDraft_OnDraftWithoutReviewHistory_Succeeds()
    {
        var (_, rev) = NewRevision();

        rev.UpdateDraft("Nội dung mới", "{}", "HASH2", Guid.NewGuid());

        rev.Summary.Should().Be("Nội dung mới");
        rev.Events.Should().Contain(e => e.Type == BibleRevisionEventType.DraftOverwritten);
    }
}
