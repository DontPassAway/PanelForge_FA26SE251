using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Bible.Commands.AddBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.UploadBibleReferenceImage;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

/// <summary>Q20: Artist nộp Concept kèm sửa nội dung (đường AddBibleEntryRevision) → SHA ảnh do server lấy từ kho Bible.</summary>
public class BibleConceptServerShaTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IAssetStorage> _storage = new();
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();
    private readonly BibleEntry _entry;
    private readonly BibleEntryRevision _v1;

    public BibleConceptServerShaTests()
    {
        var bible = SeriesBible.Create(_seriesId, Guid.NewGuid(), "Master Bible");
        _entry = bible.AddEntry(BibleEntryCategory.Character, "CHR-01", "Naruto", "Ninja làng Lá");
        _v1 = _entry.AddRevision("v1", "{}", "hash-v1", isInitialVersion: true);
        _db.SeriesBibles.Add(bible);
        _db.SaveChanges();
    }

    private Task<Result<PanelForge.Application.Features.Bible.Models.BibleEntryRevisionDto>> SubmitWithContent(string sha) =>
        new AddBibleEntryRevisionCommandHandler(_db, _storage.Object).Handle(new AddBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: _entry.Id,
            UserId: _artistId,
            Summary: "Nộp concept",
            Description: "Ninja làng Lá, áo cam",
            ReferenceImageUrl: $"/api/series/{_seriesId}/bible/assets/{sha}",
            ArtistUserId: _artistId,
            TargetRevisionId: _v1.Id), default);

    [Fact]
    public async Task ConceptWithContentChange_RecordsShaOfStoredBibleAsset()
    {
        var sha = new string('d', 64);
        _storage.Setup(s => s.OpenReadAsync(UploadBibleReferenceImageCommandHandler.StorageKey(_seriesId, sha), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream([1]));

        var result = await SubmitWithContent(sha);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var concept = await _db.BibleRevisionEvents.SingleAsync(e => e.Type == BibleRevisionEventType.ConceptSubmitted);
        concept.AssetSha256.Should().Be(sha);
        concept.HasValidHash().Should().BeTrue();
    }

    [Fact]
    public async Task ConceptWithContentChange_MissingBibleAsset_IsRejectedBeforeAnyWrite()
    {
        var result = await SubmitWithContent(new string('e', 64));

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await _db.BibleRevisionEvents.AnyAsync(e => e.Type == BibleRevisionEventType.ConceptSubmitted)).Should().BeFalse();
        (await _db.BibleEntryRevisions.AsNoTracking().SingleAsync()).SnapshotJson.Should().Be("{}");
    }
}
