using FluentAssertions;
using Moq;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Commands.ConceptComments;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

/// <summary>Q20: góp ý concept báo Artist đã nộp concept; trả lời báo Artist và mọi người trong luồng, trừ người gây ra.</summary>
public class ConceptCommentNotificationTests
{
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();
    private readonly Guid _writerId = Guid.NewGuid();
    private readonly Guid _producerId = Guid.NewGuid();
    private readonly List<BibleConceptComment> _comments = [];
    private readonly List<Notification> _notifications = [];
    private readonly Mock<IPanelForgeDbContext> _db = new();
    private readonly BibleEntry _heroEntry;
    private readonly BibleEntryRevision _heroV1;

    public ConceptCommentNotificationTests()
    {
        var bible = SeriesBible.Create(_seriesId, Guid.NewGuid(), "Master Bible");
        _heroEntry = bible.AddEntry(BibleEntryCategory.Character, "HERO", "Naruto", "Nhẫn giả Làng Lá");
        _heroV1 = _heroEntry.AddRevision("v1", "{}", "hash-hero-v1", isInitialVersion: true);
        _heroV1.SubmitConcept(_artistId, "https://r2/hero.png", "v1");

        _db.Setup(d => d.SeriesBibles).Returns(DbSetMockHelper.CreateDbSetMock(new List<SeriesBible> { bible }));
        _db.Setup(d => d.BibleEntries).Returns(DbSetMockHelper.CreateDbSetMock(new List<BibleEntry> { _heroEntry }));
        _db.Setup(d => d.BibleEntryRevisions).Returns(DbSetMockHelper.CreateDbSetMock(new List<BibleEntryRevision> { _heroV1 }));
        _db.Setup(d => d.BibleConceptComments).Returns(DbSetMockHelper.CreateDbSetMock(_comments));
        _db.Setup(d => d.Notifications).Returns(DbSetMockHelper.CreateDbSetMock(_notifications));
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    [Fact]
    public async Task CreateComment_NotifiesArtistOnly()
    {
        var handler = new CreateBibleConceptCommentCommandHandler(_db.Object);
        var cmd = new CreateBibleConceptCommentCommand(_seriesId, _heroEntry.Id, _heroV1.Id, _writerId,
            new CreateConceptCommentRequest("Mắt cần thêm tia sáng"));

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _notifications.Should().ContainSingle(n =>
            n.UserId == _artistId && n.Type == NotificationType.ConceptCommentAdded);
    }

    [Fact]
    public async Task CreateComment_ByArtistOnOwnConcept_NotifiesNobody()
    {
        var handler = new CreateBibleConceptCommentCommandHandler(_db.Object);
        var cmd = new CreateBibleConceptCommentCommand(_seriesId, _heroEntry.Id, _heroV1.Id, _artistId,
            new CreateConceptCommentRequest("Ghi chú cho chính mình"));

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task ReplyComment_NotifiesArtistAndThreadParticipants_ExceptActor()
    {
        var root = BibleConceptComment.Create(_heroV1.Id, _heroV1.CurrentConceptEventId!.Value, _writerId, "Mắt cần thêm tia sáng");
        _comments.Add(root);
        // Producer đã trả lời trước đó trong cùng luồng: phải được báo khi có trả lời mới.
        _comments.Add(BibleConceptComment.Create(_heroV1.Id, root.ConceptEventId, _producerId, "Đồng ý", parentCommentId: root.Id));

        var handler = new ReplyBibleConceptCommentCommandHandler(_db.Object);
        var cmd = new ReplyBibleConceptCommentCommand(_seriesId, _heroEntry.Id, _heroV1.Id, root.Id, _artistId,
            new ReplyConceptCommentRequest("Đã ghi nhận, sẽ thêm ở v2"));

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _notifications.Should().OnlyContain(n => n.Type == NotificationType.ConceptCommentReplied);
        _notifications.Select(n => n.UserId).Should().BeEquivalentTo(new[] { _writerId, _producerId });
    }
}
