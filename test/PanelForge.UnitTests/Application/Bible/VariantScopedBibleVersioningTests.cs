using FluentAssertions;
using MediatR;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Commands.AddBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.SubmitBibleConcept;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

public class VariantScopedBibleVersioningTests
{
    private readonly Mock<IPanelForgeDbContext> _db = new();
    private readonly Mock<ISender> _sender = new();
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();
    private readonly Guid _writerId = Guid.NewGuid();
    private readonly Guid _editorId = Guid.NewGuid();
    private readonly SeriesBible _bible;
    private readonly BibleEntry _characterEntry;
    private readonly BibleEntryRevision _defaultV1;

    public VariantScopedBibleVersioningTests()
    {
        _bible = SeriesBible.Create(_seriesId, Guid.NewGuid(), "Master Bible");
        _characterEntry = _bible.AddEntry(BibleEntryCategory.Character, "CHAR_01", "Akira", "Nhân vật chính");
        _defaultV1 = _characterEntry.AddRevision("v1", "{\"appearance\":\"Ao khoac den\"}", "hash-default-v1", isInitialVersion: true, stageName: "Mặc định");

        var entries = new List<BibleEntry> { _characterEntry };
        var revisions = new List<BibleEntryRevision> { _defaultV1 };

        _db.Setup(d => d.SeriesBibles).Returns(DbSetMockHelper.CreateDbSetMock(new List<SeriesBible> { _bible }));
        _db.Setup(d => d.BibleEntries).Returns(DbSetMockHelper.CreateDbSetMock(entries));
        _db.Setup(d => d.BibleEntryRevisions).Returns(DbSetMockHelper.CreateDbSetMock(revisions));
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    [Fact]
    public async Task AddBibleEntryRevision_WhenNewVariantCreated_StartsAtVersion1()
    {
        var handler = new AddBibleEntryRevisionCommandHandler(_db.Object);
        var command = new AddBibleEntryRevisionCommand(
            _seriesId,
            _characterEntry.Id,
            _writerId,
            Summary: "Tạo biến thể Lúc nhỏ",
            Name: "Akira",
            Description: "Luc nho mac ao len do",
            StageName: "Lúc nhỏ",
            IsNewVariant: true
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().NotBeNull();
        result.Value!.StageName.Should().Be("Lúc nhỏ");
        result.Value.VersionNumber.Should().Be(1);

        _characterEntry.Revisions.Should().HaveCount(2);
        var kidRev = _characterEntry.Revisions.Single(r => r.StageName == "Lúc nhỏ");
        kidRev.VersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task AddBibleEntryRevision_WhenDuplicateStageName_ReturnsConflict()
    {
        var handler = new AddBibleEntryRevisionCommandHandler(_db.Object);
        var command = new AddBibleEntryRevisionCommand(
            _seriesId,
            _characterEntry.Id,
            _writerId,
            Summary: "Tạo biến thể trùng tên",
            Name: "Akira",
            Description: "Biến thể trùng",
            StageName: "mặc định", // Trùng với "Mặc định" (case-insensitive)
            IsNewVariant: true
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("đã tồn tại");
    }

    [Fact]
    public async Task AddBibleEntryRevision_WhenSuperseding_IncrementsVersionNumberOnlyForSameStage()
    {
        // 1. Tạo biến thể "Lúc nhỏ" (v1)
        var kidV1 = _characterEntry.AddRevision("v1", "{\"appearance\":\"Luc nho\"}", "hash-kid-v1", isInitialVersion: true, stageName: "Lúc nhỏ");

        // Khóa Canon cho bản "Mặc định" v1
        _defaultV1.Lock(_editorId);

        var handler = new AddBibleEntryRevisionCommandHandler(_db.Object);

        // 2. Supersede bản "Mặc định" v1 -> Phải ra "Mặc định" v2
        var supersedeDefaultCmd = new AddBibleEntryRevisionCommand(
            _seriesId,
            _characterEntry.Id,
            _writerId,
            Summary: "Thay thế bản mặc định",
            Name: "Akira",
            Description: "Ao khoac moi",
            StageName: "Mặc định",
            SupersedesRevisionId: _defaultV1.Id,
            SupersedeReason: "Cập nhật tạo hình mới",
            ExpectedVersionNumber: 1
        );

        var defaultResult = await handler.Handle(supersedeDefaultCmd, CancellationToken.None);

        defaultResult.IsSuccess.Should().BeTrue(defaultResult.ErrorMessage);
        defaultResult.Value!.StageName.Should().Be("Mặc định");
        defaultResult.Value.VersionNumber.Should().Be(2);

        // Biến thể "Lúc nhỏ" vẫn giữ nguyên v1
        kidV1.VersionNumber.Should().Be(1);
        kidV1.StageName.Should().Be("Lúc nhỏ");
    }

    [Fact]
    public async Task SubmitConcept_AfterReworkRequired_IncrementsCurrentConceptVersionNumberWithoutChangingRevisionIdOrVersionNumber()
    {
        // Nộp lần 1 (ConceptVersionNumber = 1)
        _defaultV1.SubmitConcept(_artistId, "https://r2/akira-c1.png", "Bản phác thảo 1", "sha256-c1");
        _defaultV1.CurrentConceptVersionNumber.Should().Be(1);
        var initialRevisionId = _defaultV1.Id;
        var initialVersionNumber = _defaultV1.VersionNumber;

        // Editor yêu cầu sửa lại (ReworkRequired)
        _defaultV1.RequestRework(_editorId, "Vẽ lại áo khoác");
        _defaultV1.ReviewStatus.Should().Be(BibleRevisionStatus.ReworkRequired);

        // Artist nộp lại sau ReworkRequired
        var handler = new SubmitBibleConceptCommandHandler(_db.Object, _sender.Object);
        var reworkSubmitCmd = new SubmitBibleConceptCommand(
            _seriesId,
            _characterEntry.Id,
            _artistId,
            new SubmitConceptArtRequest(
                ReferenceImageUrl: "https://r2/akira-c2.png",
                Summary: "Bản vẽ lần 2 sau khi sửa áo khoác",
                AssetSha256: "sha256-c2"
            )
        );

        var result = await handler.Handle(reworkSubmitCmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        _defaultV1.Id.Should().Be(initialRevisionId);
        _defaultV1.VersionNumber.Should().Be(initialVersionNumber);
        _defaultV1.CurrentConceptVersionNumber.Should().Be(2);
        _defaultV1.ReviewStatus.Should().Be(BibleRevisionStatus.ConceptSubmitted);
    }

    [Fact]
    public async Task AddBibleEntryRevision_WhenEditingDraftInPlace_DoesNotIncrementVersionNumber()
    {
        // _defaultV1 đang ở trạng thái Draft
        _defaultV1.ReviewStatus.Should().Be(BibleRevisionStatus.Draft);

        var handler = new AddBibleEntryRevisionCommandHandler(_db.Object);
        var editDraftCmd = new AddBibleEntryRevisionCommand(
            _seriesId,
            _characterEntry.Id,
            _writerId,
            Summary: "Sửa nội dung nháp",
            Name: "Akira",
            Description: "Ao khoac sua lai trong ban nhap",
            StageName: "Mặc định",
            TargetRevisionId: _defaultV1.Id,
            ExpectedVersionNumber: 1
        );

        var result = await handler.Handle(editDraftCmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.VersionNumber.Should().Be(1);
        result.Value.Id.Should().Be(_defaultV1.Id);
        _defaultV1.SnapshotJson.Should().Contain("Ao khoac sua lai trong ban nhap");
        _characterEntry.Revisions.Should().HaveCount(1);
    }
}
