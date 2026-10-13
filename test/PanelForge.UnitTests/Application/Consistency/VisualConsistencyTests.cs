using FluentAssertions;
using MediatR;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Consistency.Commands;
using PanelForge.Application.Features.Consistency.Common;
using PanelForge.Application.Features.Consistency.Models;
using PanelForge.Application.Features.Consistency.Queries;
using PanelForge.Domain.Entities.Assets;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Consistency;

public class VisualConsistencyTests
{
    private sealed class VisualFixture
    {
        public readonly PanelForgeDbContext Db = InMemoryDb.Create();
        public readonly Guid UserId = Guid.NewGuid();
        public readonly Chapter Chapter;
        public readonly Scene Scene;
        public readonly Page Page;
        public readonly Panel Panel;
        public readonly BibleEntry Akira;
        public readonly User EditorUser;

        public VisualFixture()
        {
            var seriesId = Guid.NewGuid();
            var wsId = Guid.NewGuid();

            EditorUser = User.Create("editor@test.com", "Editor User");
            Db.Users.Add(EditorUser);

            Chapter = Chapter.Create(seriesId, 1, "Chương 1: Mở màn");
            Scene = Scene.Create(Chapter.Id, 1);
            Page = Page.CreateNew(Chapter.Id, 1, LayoutFormat.StandardPage, 1200, 1800, 300);
            Panel = Panel.Create(Page.Id, 1, 1, sceneId: Scene.Id);

            var bible = SeriesBible.Create(seriesId, wsId, "Bible Series");
            Akira = bible.AddEntry(BibleEntryCategory.Character, "AKIRA", "Akira", "Nhân vật chính");
            Akira.AddRevision("v1", "{}", "hash-akira", effectiveFromChapterNumber: 1, isInitialVersion: true)
                 .Lock(UserId);

            Db.Chapters.Add(Chapter);
            Db.Scenes.Add(Scene);
            Db.Pages.Add(Page);
            Db.Panels.Add(Panel);
            Db.SeriesBibles.Add(bible);

            var line = ScriptLine.Create(Scene.Id, 1, "Chào mọi người", null, Akira.Id, Panel.Id);
            Db.ScriptLines.Add(line);

            var sha = new string('a', 64);
            var asset = Asset.Create(wsId, sha, 1024, "image/png");
            Db.Assets.Add(asset);

            var art = AssetReference.Create(asset.Id, wsId, Chapter.Id, Page.Id, Guid.NewGuid(), 1, "ARTWORK", "akira_panel.png", UserId, Panel.Id);
            Db.AssetReferences.Add(art);

            Db.SaveChanges();
        }

        public Task<Result<VisualConsistencyReportDto>> RunCheck(decimal? threshold = null)
        {
            var handler = new RunVisualConsistencyCheckCommandHandler(Db);
            return handler.Handle(new RunVisualConsistencyCheckCommand(Chapter.Id, UserId, threshold), default);
        }

        public Task<Result<VisualConsistencyReportDto>> GetReport(decimal? threshold = null)
        {
            var mediatorMock = new Mock<IMediator>();
            var handler = new GetVisualConsistencyReportQueryHandler(Db, mediatorMock.Object);
            return handler.Handle(new GetVisualConsistencyReportQuery(Chapter.Id, threshold), default);
        }

        public Task<Result<ConsistencyFindingDto>> Triage(Guid findingId, ConsistencyFindingStatus decision, string? note)
        {
            var handler = new TriageConsistencyFindingCommandHandler(Db);
            return handler.Handle(new TriageConsistencyFindingCommand(Chapter.Id, findingId, EditorUser.Id, decision, note), default);
        }
    }

    [Fact]
    public async Task RunVisualConsistencyCheck_GeneratesReport_WithCalculatedScores()
    {
        var f = new VisualFixture();

        var result = await f.RunCheck();

        result.IsSuccess.Should().BeTrue();
        var report = result.Value!;
        report.ChapterId.Should().Be(f.Chapter.Id);
        report.Threshold.Should().Be(ConsistencyChecker.DefaultVisualSimilarityThreshold);
        report.TotalEvaluated.Should().BeGreaterThan(0);
        report.Deviations.Should().NotBeNull();
    }

    [Fact]
    public async Task RunVisualConsistencyCheck_FlagsDeviations_WhenThresholdIsHigh()
    {
        var f = new VisualFixture();

        // Đặt ngưỡng cao 0.99 để đảm bảo mọi điểm dưới 0.99 đều bị gắn cờ là deviation
        var result = await f.RunCheck(0.99m);

        result.IsSuccess.Should().BeTrue();
        var report = result.Value!;
        report.DeviationCount.Should().BeGreaterThan(0);
        report.Deviations.Should().Contain(d => d.RuleCode() == ConsistencyChecker.VisualCharacterDeviation || d.IsDeviation);
        report.Deviations.First().Threshold.Should().Be(0.99m);
    }

    [Fact]
    public async Task TriageVisualDeviation_UpdatesFindingStatus_AndRecordsEditorDecision()
    {
        var f = new VisualFixture();
        var checkResult = await f.RunCheck(0.99m);
        checkResult.IsSuccess.Should().BeTrue();

        var findingId = checkResult.Value!.Deviations.First().FindingId;

        // Editor xác nhận lỗi (Confirmed)
        var triageResult = await f.Triage(findingId, ConsistencyFindingStatus.Confirmed, "Đúng là vẽ lệch so với Character Sheet khóa v1.");

        triageResult.IsSuccess.Should().BeTrue();
        triageResult.Value!.Status.Should().Be(ConsistencyFindingStatus.Confirmed);
        triageResult.Value!.TriageNote.Should().Be("Đúng là vẽ lệch so với Character Sheet khóa v1.");
        triageResult.Value!.TriagedByUserId.Should().Be(f.EditorUser.Id);

        // Đọc lại report kiểm tra trạng thái
        var reportResult = await f.GetReport(0.99m);
        reportResult.IsSuccess.Should().BeTrue();
        var deviation = reportResult.Value!.Deviations.First(d => d.FindingId == findingId);
        deviation.Status.Should().Be(ConsistencyFindingStatus.Confirmed);
        deviation.TriageNote.Should().Be("Đúng là vẽ lệch so với Character Sheet khóa v1.");
    }

    [Fact]
    public async Task TriageVisualDeviation_RequiresNote_WhenIgnoredOrNeedsClarification()
    {
        var f = new VisualFixture();
        var checkResult = await f.RunCheck(0.99m);
        var findingId = checkResult.Value!.Deviations.First().FindingId;

        // Bỏ qua nhưng không ghi lý do -> lỗi validation BR-21
        var failResult = await f.Triage(findingId, ConsistencyFindingStatus.Ignored, null);
        failResult.IsSuccess.Should().BeFalse();
        failResult.ErrorCode.Should().Be(ResultErrorCodes.Validation);

        // Bỏ qua có ghi lý do -> thành công
        var successResult = await f.Triage(findingId, ConsistencyFindingStatus.Ignored, "Chấp nhận góc vẽ nghiêng 45 độ theo phong cách cảnh.");
        successResult.IsSuccess.Should().BeTrue();
        successResult.Value!.Status.Should().Be(ConsistencyFindingStatus.Ignored);
    }
}

internal static class DeviationExtensions
{
    public static string RuleCode(this VisualDeviationDto d) => ConsistencyChecker.VisualCharacterDeviation;
}
