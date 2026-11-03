using FluentAssertions;
using MediatR;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Commands.AddBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.SubmitBibleConcept;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

/// <summary>
/// Phiên bản nội dung tách khỏi trạng thái duyệt: nộp/nộp lại Concept chỉ thêm sự kiện vào phiên bản hiện hành,
/// không tăng VersionNumber, không đổi ContentHash (BR-01). Sửa nội dung mới tạo phiên bản. Bản đã khoá → 409.
/// </summary>
public class BibleConceptVersioningTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<ISender> _sender = new();
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();
    private readonly Guid _editorId = Guid.NewGuid();
    private readonly BibleEntry _entry;
    private readonly BibleEntryRevision _v1;

    public BibleConceptVersioningTests()
    {
        var bible = SeriesBible.Create(_seriesId, Guid.NewGuid(), "Bible");
        _entry = bible.AddEntry(BibleEntryCategory.Character, "A_DUOC", "A Dược", "Thiếu niên luyện đan");
        _v1 = _entry.AddRevision("Khởi tạo mục A Dược", "{}", "C9279D77", authorId: Guid.NewGuid(), isInitialVersion: true);
        _db.SeriesBibles.Add(bible);
        _db.SaveChanges();
    }

    private Task<Result<BibleEntryRevisionDto>> Submit(string image = "/assets/concept-1.png", string? description = null) =>
        new SubmitBibleConceptCommandHandler(_db, _sender.Object).Handle(
            new SubmitBibleConceptCommand(_seriesId, _entry.Id, _artistId,
                new SubmitConceptArtRequest(ReferenceImageUrl: image, Note: "Nộp bản vẽ", Description: description)),
            default);

    [Fact]
    public async Task ImageOnlyConcept_AddsEvent_ToSameVersion()
    {
        var result = await Submit();

        result.IsSuccess.Should().BeTrue();
        result.Value!.VersionNumber.Should().Be(1);
        result.Value.ReviewStatus.Should().Be(BibleRevisionStatus.ConceptSubmitted);
        result.Value.ConceptImageUrl.Should().Be("/assets/concept-1.png");
        result.Value.Events!.Select(e => e.Type).Should().Equal(BibleRevisionEventType.Created, BibleRevisionEventType.ConceptSubmitted);
        _db.BibleEntryRevisions.Should().ContainSingle();
        _v1.ContentHash.Should().Be("C9279D77");
        _entry.ReferenceImageUrl.Should().Be("/assets/concept-1.png");
        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReworkThenResubmit_StaysOneVersion_WithFullTimeline()
    {
        await Submit("/assets/concept-1.png");
        _v1.RequestRework(_editorId, "Tóc phải màu bạc");
        await _db.SaveChangesAsync();

        var result = await Submit("/assets/concept-2.png");

        _db.BibleEntryRevisions.Should().ContainSingle();
        result.Value!.ReworkFeedback.Should().BeNull();
        result.Value.Events!.Select(e => e.Type).Should().Equal(
            BibleRevisionEventType.Created, BibleRevisionEventType.ConceptSubmitted,
            BibleRevisionEventType.ReworkRequested, BibleRevisionEventType.ConceptSubmitted);
        result.Value.Events![2].Comment.Should().Be("Tóc phải màu bạc"); // nhận xét cũ không bị ghi đè
        result.Value.Events.Should().OnlyContain(e => e.EventHash.Length == 64);
    }

    [Fact]
    public async Task LockedVersion_RejectsConcept_WithConflict()
    {
        await Submit();
        _v1.Lock(Guid.NewGuid());
        await _db.SaveChangesAsync();

        var result = await Submit("/assets/concept-2.png");

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _v1.ConceptImageUrl.Should().Be("/assets/concept-1.png");
        // Sự kiện khoá chốt ảnh canon
        _v1.Events.Single(e => e.Type == BibleRevisionEventType.Locked).ImageUrl.Should().Be("/assets/concept-1.png");
    }

    [Fact]
    public async Task ConceptWithContentChange_GoesThroughNewVersionPath()
    {
        _sender.Setup(s => s.Send(It.IsAny<AddBibleEntryRevisionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BibleEntryRevisionDto>.Failure("stub"));

        await Submit(description: "Thiếu niên luyện đan, mắt đỏ");

        _sender.Verify(s => s.Send(It.Is<AddBibleEntryRevisionCommand>(c =>
            c.ArtistUserId == _artistId && c.ReferenceImageUrl == "/assets/concept-1.png"), It.IsAny<CancellationToken>()));
        _v1.Events.Should().ContainSingle(e => e.Type == BibleRevisionEventType.Created);
    }
}
