using System.Text.Json;
using FluentAssertions;
using MediatR;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Bible.Commands.ConceptComments;
using PanelForge.Application.Features.Bible.Commands.ConfirmBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.ReviewBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.SubmitBibleConcept;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

public class BibleConceptVersioningAndConfirmTests
{
    private readonly Mock<IPanelForgeDbContext> _db = new();
    private readonly Mock<ISender> _sender = new();
    private readonly Mock<IChapterCastInheritanceResolver> _inheritanceResolver = new();
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly Guid _editorId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();
    private readonly Guid _writerId = Guid.NewGuid();
    private readonly Guid _producerId = Guid.NewGuid();
    private readonly SeriesBible _bible;
    private readonly BibleEntry _heroEntry;
    private readonly BibleEntry _stubEntry;
    private readonly BibleEntryRevision _heroV1;

    public BibleConceptVersioningAndConfirmTests()
    {
        _bible = SeriesBible.Create(_seriesId, Guid.NewGuid(), "Master Bible");
        _heroEntry = _bible.AddEntry(BibleEntryCategory.Character, "HERO", "Naruto", "Nhẫn giả Làng Lá");
        _heroV1 = _heroEntry.AddRevision("v1", "{}", "hash-hero-v1", isInitialVersion: true);

        _stubEntry = BibleEntry.CreateStub(_bible.Id, BibleEntryCategory.Character, "Sasuke");
        var stubV1 = _stubEntry.AddRevision("v1", "{}", "hash-stub", isInitialVersion: true);

        var entries = new List<BibleEntry> { _heroEntry, _stubEntry };
        var revisions = new List<BibleEntryRevision> { _heroV1, stubV1 };
        var comments = new List<BibleConceptComment>();

        _db.Setup(d => d.SeriesBibles).Returns(DbSetMockHelper.CreateDbSetMock(new List<SeriesBible> { _bible }));
        _db.Setup(d => d.BibleEntries).Returns(DbSetMockHelper.CreateDbSetMock(entries));
        _db.Setup(d => d.BibleEntryRevisions).Returns(DbSetMockHelper.CreateDbSetMock(revisions));
        _db.Setup(d => d.BibleConceptComments).Returns(DbSetMockHelper.CreateDbSetMock(comments));
        _db.Setup(d => d.Notifications).Returns(DbSetMockHelper.CreateDbSetMock(new List<Notification>()));
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    [Fact]
    public async Task SubmitConcept_WhenEntryIsStub_ReturnsConflict()
    {
        var handler = new SubmitBibleConceptCommandHandler(_db.Object, _sender.Object);
        var cmd = new SubmitBibleConceptCommand(_seriesId, _stubEntry.Id, _artistId,
            new SubmitConceptArtRequest(ReferenceImageUrl: "https://r2/sasuke.png"));

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("chưa được Writer viết định nghĩa");
    }

    [Fact]
    public async Task SubmitConcept_WhenEntryDescriptionIsEmpty_ReturnsConflict()
    {
        var emptyDescEntry = BibleEntry.CreateStub(_bible.Id, BibleEntryCategory.Character, "Empty", "");
        emptyDescEntry.MarkAsStub(false);
        emptyDescEntry.AddRevision("v1", "{}", "hash-empty", isInitialVersion: true);

        _db.Setup(d => d.BibleEntries).Returns(DbSetMockHelper.CreateDbSetMock(new List<BibleEntry> { emptyDescEntry }));

        var handler = new SubmitBibleConceptCommandHandler(_db.Object, _sender.Object);
        var cmd = new SubmitBibleConceptCommand(_seriesId, emptyDescEntry.Id, _artistId,
            new SubmitConceptArtRequest(ReferenceImageUrl: "https://r2/empty.png"));

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("chưa được Writer viết định nghĩa");
    }

    [Fact]
    public async Task SubmitConcept_IncrementsConceptVersionNumber_AndSetsCurrentConceptEvent()
    {
        var handler = new SubmitBibleConceptCommandHandler(_db.Object, _sender.Object);

        // Nộp concept lần đầu (v1)
        var cmd1 = new SubmitBibleConceptCommand(_seriesId, _heroEntry.Id, _artistId,
            new SubmitConceptArtRequest(ReferenceImageUrl: "https://r2/hero-v1.png", AssetSha256: "sha256-v1"));
        var res1 = await handler.Handle(cmd1, CancellationToken.None);

        res1.IsSuccess.Should().BeTrue();
        _heroV1.ReviewStatus.Should().Be(BibleRevisionStatus.ConceptSubmitted);
        _heroV1.CurrentConceptEventId.Should().NotBeNull();

        var evt1 = _heroV1.Events.Single(e => e.Type == BibleRevisionEventType.ConceptSubmitted);
        evt1.ConceptVersionNumber.Should().Be(1);
        evt1.AssetSha256.Should().Be("sha256-v1");

        // Artist nộp đè concept mới (v2) trước khi Editor duyệt
        var cmd2 = new SubmitBibleConceptCommand(_seriesId, _heroEntry.Id, _artistId,
            new SubmitConceptArtRequest(ReferenceImageUrl: "https://r2/hero-v2.png", AssetSha256: "sha256-v2"));
        var res2 = await handler.Handle(cmd2, CancellationToken.None);

        res2.IsSuccess.Should().BeTrue();
        var conceptEvents = _heroV1.Events.Where(e => e.Type == BibleRevisionEventType.ConceptSubmitted).OrderBy(e => e.CreatedAt).ToList();
        conceptEvents.Should().HaveCount(2);
        conceptEvents[1].ConceptVersionNumber.Should().Be(2);
        conceptEvents[1].AssetSha256.Should().Be("sha256-v2");
        _heroV1.CurrentConceptEventId.Should().Be(conceptEvents[1].Id);
    }

    [Fact]
    public async Task ConfirmCanon_ByEditor_LocksCanonAndSetsLockedEvent()
    {
        // Artist nộp concept
        _heroV1.SubmitConcept(_artistId, "https://r2/hero.png", "Bản hoàn chỉnh", "sha-concept-1");

        var handler = new ConfirmBibleEntryRevisionCommandHandler(_db.Object, _inheritanceResolver.Object);
        var cmd = new ConfirmBibleEntryRevisionCommand(_seriesId, _heroEntry.Id, _heroV1.Id, _editorId, _heroV1.CurrentConceptEventId);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        _heroV1.IsLocked.Should().BeTrue();
        _heroV1.ReviewStatus.Should().Be(BibleRevisionStatus.CanonLocked);
        _heroV1.LockedById.Should().Be(_editorId);
        _heroV1.LockedAt.Should().NotBeNull();

        var lockedEvt = _heroV1.Events.Single(e => e.Type == BibleRevisionEventType.Locked);
        lockedEvt.ActorId.Should().Be(_editorId);
        lockedEvt.ConceptVersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task ConfirmCanon_UpdatesEntryCacheFromLockedRevision()
    {
        var revision = _heroEntry.AddRevision(
            "v2",
            JsonSerializer.Serialize(new
            {
                Name = _heroEntry.Name,
                Description = "Mô tả Canon mới",
                DetailsJson = "{\"age\":18}",
                ReferenceImageUrl = "draft.png"
            }),
            "hash-v2");
        revision.SubmitConcept(_artistId, "canon-v2.png", "Bản hoàn chỉnh");

        var handler = new ConfirmBibleEntryRevisionCommandHandler(_db.Object, _inheritanceResolver.Object);
        var result = await handler.Handle(
            new ConfirmBibleEntryRevisionCommand(_seriesId, _heroEntry.Id, revision.Id, _editorId, revision.CurrentConceptEventId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        _heroEntry.Name.Should().Be("Naruto");
        _heroEntry.Description.Should().Be("Mô tả Canon mới");
        _heroEntry.ReferenceImageUrl.Should().Be("canon-v2.png");
    }

    [Fact]
    public async Task ConfirmCanon_WhenConceptEventMismatched_ReturnsConflict()
    {
        // Artist nộp concept v1
        _heroV1.SubmitConcept(_artistId, "https://r2/hero-v1.png", "v1");
        var oldEventId = _heroV1.CurrentConceptEventId!.Value;

        // Artist nộp đè v2
        _heroV1.SubmitConcept(_artistId, "https://r2/hero-v2.png", "v2");

        // Editor gửi xác nhận nhưng nhắm vào oldEventId
        var handler = new ConfirmBibleEntryRevisionCommandHandler(_db.Object, _inheritanceResolver.Object);
        var cmd = new ConfirmBibleEntryRevisionCommand(_seriesId, _heroEntry.Id, _heroV1.Id, _editorId, oldEventId);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("Artist vừa nộp bản mới");
        _heroV1.IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmCanon_WhenEditorIsArtistWhoSubmittedConcept_ReturnsForbidden()
    {
        // Artist tự mình là Editor và nộp concept
        _heroV1.SubmitConcept(_artistId, "https://r2/hero.png", "v1");

        var handler = new ConfirmBibleEntryRevisionCommandHandler(_db.Object, _inheritanceResolver.Object);
        var cmd = new ConfirmBibleEntryRevisionCommand(_seriesId, _heroEntry.Id, _heroV1.Id, _artistId, _heroV1.CurrentConceptEventId);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("BR-06");
        _heroV1.IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmCanon_WhenRevisionNotConceptSubmitted_ReturnsConflict()
    {
        // Revision đang ở Draft
        var handler = new ConfirmBibleEntryRevisionCommandHandler(_db.Object, _inheritanceResolver.Object);
        var cmd = new ConfirmBibleEntryRevisionCommand(_seriesId, _heroEntry.Id, _heroV1.Id, _editorId);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _heroV1.IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task RequestRework_ByProducer_TransitionsToReworkRequired_AndCreatesRootComment()
    {
        _heroV1.SubmitConcept(_artistId, "https://r2/hero.png", "v1");
        var conceptEventId = _heroV1.CurrentConceptEventId!.Value;

        var handler = new RequestReworkBibleEntryRevisionCommandHandler(_db.Object);
        var cmd = new RequestReworkBibleEntryRevisionCommand(_seriesId, _heroEntry.Id, _heroV1.Id, _producerId, "Màu áo chưa đúng cốt truyện");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _heroV1.ReviewStatus.Should().Be(BibleRevisionStatus.ReworkRequired);
        _heroV1.ReworkFeedback.Should().Be("Màu áo chưa đúng cốt truyện");

        _db.Verify(d => d.BibleConceptComments.Add(It.Is<BibleConceptComment>(c =>
            c.RevisionId == _heroV1.Id
            && c.ConceptEventId == conceptEventId
            && c.AuthorId == _producerId
            && c.Body.Contains("Màu áo chưa đúng cốt truyện"))), Times.Once);
    }

    [Fact]
    public async Task ReplyConceptComment_DoesNotChangeRevisionReviewStatus()
    {
        _heroV1.SubmitConcept(_artistId, "https://r2/hero.png", "v1");
        var conceptEventId = _heroV1.CurrentConceptEventId!.Value;

        var rootComment = BibleConceptComment.Create(_heroV1.Id, conceptEventId, _writerId, "Mắt cần thêm tia sáng");

        var comments = new List<BibleConceptComment> { rootComment };
        _db.Setup(d => d.BibleConceptComments).Returns(DbSetMockHelper.CreateDbSetMock(comments));

        var handler = new ReplyBibleConceptCommentCommandHandler(_db.Object);
        var replyCmd = new ReplyBibleConceptCommentCommand(_seriesId, _heroEntry.Id, _heroV1.Id, rootComment.Id, _artistId,
            new ReplyConceptCommentRequest("Đã ghi nhận, sẽ thêm tia sáng ở v2"));

        var result = await handler.Handle(replyCmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Body.Should().Be("Đã ghi nhận, sẽ thêm tia sáng ở v2");
        _heroV1.ReviewStatus.Should().Be(BibleRevisionStatus.ConceptSubmitted, "Trả lời góp ý tuyệt đối không thay đổi trạng thái của revision");
    }

    [Fact]
    public async Task ResolveConceptComment_MarksResolvedAtAndResolvedBy()
    {
        _heroV1.SubmitConcept(_artistId, "https://r2/hero.png", "v1");
        var conceptEventId = _heroV1.CurrentConceptEventId!.Value;

        var rootComment = BibleConceptComment.Create(_heroV1.Id, conceptEventId, _writerId, "Nhận xét cần sửa");

        var comments = new List<BibleConceptComment> { rootComment };
        _db.Setup(d => d.BibleConceptComments).Returns(DbSetMockHelper.CreateDbSetMock(comments));

        var handler = new ResolveBibleConceptCommentCommandHandler(_db.Object);
        var resolveCmd = new ResolveBibleConceptCommentCommand(_seriesId, _heroEntry.Id, _heroV1.Id, rootComment.Id, _editorId, Resolve: true);

        var result = await handler.Handle(resolveCmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        rootComment.ResolvedAt.Should().NotBeNull();
        rootComment.ResolvedBy.Should().Be(_editorId);
    }
}
