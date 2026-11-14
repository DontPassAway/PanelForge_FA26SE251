using FluentAssertions;
using PanelForge.Application.Features.Bible.Queries.GetBibleDesignQueue;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

public class GetBibleDesignQueueTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly BibleEntry _character;
    private readonly BibleEntry _location;
    private readonly BibleEntry _prop;
    private readonly BibleEntry _stub;
    private readonly BibleEntry _crowd;
    private readonly BibleEntry _terminology;
    private readonly Chapter _ch1;
    private readonly Chapter _ch2;
    private readonly Chapter _ch3Frozen;

    public GetBibleDesignQueueTests()
    {
        var bible = SeriesBible.Create(_seriesId, Guid.NewGuid(), "Master Bible");

        // 1. Character hợp lệ (Draft)
        _character = bible.AddEntry(BibleEntryCategory.Character, "CHR-01", "Naruto", "Hero");
        _character.AddRevision("v1", "{}", "hash-char", authorId: _userId, stageName: "Trưởng thành", isInitialVersion: true);

        // 2. Location hợp lệ (ReworkRequired)
        _location = bible.AddEntry(BibleEntryCategory.Location, "LOC-01", "Làng Lá", "Ngôi làng ninja");
        var locRev = _location.AddRevision("v1", "{}", "hash-loc", authorId: _userId, isInitialVersion: true);
        locRev.SubmitConcept(_userId, "https://r2/loc.png", null);
        locRev.RequestRework(_userId, "Cần vẽ thêm cổng làng");

        // 3. Prop hợp lệ (ConceptSubmitted chưa khoá)
        _prop = bible.AddEntry(BibleEntryCategory.Prop, "PRP-01", "Kunai", "Phi tiêu");
        var propRev = _prop.AddRevision("v1", "{}", "hash-prop", authorId: _userId, isInitialVersion: true);
        propRev.SubmitConcept(_userId, "https://r2/kunai.png", null);

        // 4. Stub (phải bị loại)
        _stub = BibleEntry.CreateStub(bible.Id, BibleEntryCategory.Character, "Stub Ninja");
        _stub.AddRevision("v1", "{}", "hash-stub", authorId: _userId, isInitialVersion: true);

        // 5. Crowd (phải bị loại)
        _crowd = bible.AddEntry(BibleEntryCategory.Character, "CHR-CROWD", "Dân Làng", "Quần chúng");
        _crowd.SetCrowd(true);
        _crowd.AddRevision("v1", "{}", "hash-crowd", authorId: _userId, isInitialVersion: true);

        // 6. Terminology (phải bị loại vì không phải IsVisualDesign)
        _terminology = bible.AddEntry(BibleEntryCategory.Terminology, "TRM-01", "Chakra", "Năng lượng");
        _terminology.AddRevision("v1", "{}", "hash-chakra", authorId: _userId, isInitialVersion: true);

        _db.SeriesBibles.Add(bible);
        _db.BibleEntries.AddRange(_character, _location, _prop, _stub, _crowd, _terminology);

        // Chapters
        _ch1 = Chapter.Create(_seriesId, 1, "Ch1");
        _ch2 = Chapter.Create(_seriesId, 2, "Ch2");
        _ch3Frozen = Chapter.Create(_seriesId, 3, "Ch3");
        _ch3Frozen.SubmitScript(_userId);
        _ch3Frozen.StartProduction(_userId, "hash-frozen");
        _db.Chapters.AddRange(_ch1, _ch2, _ch3Frozen);

        // Ch1 và Ch2 bind _character
        _db.ChapterBibleBindings.Add(ChapterBibleBinding.Create(_ch1.Id, _character.Id, null, ChapterBibleBindingSource.Speaker));
        _db.ChapterBibleBindings.Add(ChapterBibleBinding.Create(_ch2.Id, _character.Id, null, ChapterBibleBindingSource.Speaker));
        // Ch3 (Frozen) cũng bind _character
        _db.ChapterBibleBindings.Add(ChapterBibleBinding.Create(_ch3Frozen.Id, _character.Id, null, ChapterBibleBindingSource.Speaker));

        _db.SaveChanges();
    }

    [Fact]
    public async Task GetQueue_FiltersVisualDesigns_ExcludesStubCrowdAndTextEntries()
    {
        var handler = new GetBibleDesignQueueQueryHandler(_db);
        var query = new GetBibleDesignQueueQuery(_seriesId);

        var result = await handler.Handle(query, default);

        result.IsSuccess.Should().BeTrue();
        var items = result.Value;

        // Chỉ chứa Character, Location, Prop
        items.Select(i => i.EntryId).Should().Contain(_character.Id);
        items.Select(i => i.EntryId).Should().Contain(_location.Id);
        items.Select(i => i.EntryId).Should().Contain(_prop.Id);

        // Loại bỏ Stub, Crowd, Terminology
        items.Select(i => i.EntryId).Should().NotContain(_stub.Id);
        items.Select(i => i.EntryId).Should().NotContain(_crowd.Id);
        items.Select(i => i.EntryId).Should().NotContain(_terminology.Id);
    }

    [Fact]
    public async Task GetQueue_CalculatesWaitingChapters_FromNonFrozenChaptersOnly()
    {
        var handler = new GetBibleDesignQueueQueryHandler(_db);
        var query = new GetBibleDesignQueueQuery(_seriesId);

        var result = await handler.Handle(query, default);

        result.IsSuccess.Should().BeTrue();
        var characterItem = result.Value.First(i => i.EntryId == _character.Id);

        // Ch1 (ch.1) và Ch2 (ch.2) chưa Frozen -> có trong WaitingChapterNumbers
        characterItem.WaitingChapterNumbers.Should().NotBeNull();
        characterItem.WaitingChapterNumbers.Should().Contain(new decimal[] { 1, 2 });

        // Ch3 (ch.3) đã Frozen -> KHÔNG có trong WaitingChapterNumbers
        characterItem.WaitingChapterNumbers.Should().NotContain(3);
    }
}
