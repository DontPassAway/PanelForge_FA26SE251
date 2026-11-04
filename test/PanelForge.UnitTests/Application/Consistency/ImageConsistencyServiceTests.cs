using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using PanelForge.AI.Contracts;
using PanelForge.API.Controllers;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Application.Features.Consistency.Common;
using PanelForge.Application.Features.Consistency.Models;
using PanelForge.Domain.Entities.Assets;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.Infrastructure.Services;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Consistency;

/// <summary>Kiểm ảnh panel đi qua PanelForge.AI (IAiClient.CompareImagesAsync); key chỉ lấy từ Workspace, không còn key dự phòng.</summary>
public class ImageConsistencyServiceTests
{
    private sealed class TestFixture
    {
        public readonly PanelForgeDbContext Db = InMemoryDb.Create();
        public readonly Mock<IAssetStorage> StorageMock = new();
        public readonly Mock<IAiKeyProtector> KeyProtectorMock = new();
        public readonly Mock<IAiUsageService> UsageServiceMock = new();
        public readonly Mock<IAiClient> AiClientMock = new();
        public readonly AiAgentSettings Settings = new() { Provider = "Gemini", GeminiModel = "gemini-2.5-flash" };
        public readonly Guid WorkspaceId = Guid.NewGuid();
        public readonly Guid SeriesId = Guid.NewGuid();
        public readonly Chapter Chapter;
        public readonly Page Page;
        public readonly Panel Panel;
        public readonly BibleEntry Character;
        public VisionCompareRequest? LastRequest;

        public TestFixture(VisionCompareResponse reply)
        {
            var workspace = StudioWorkspace.Create("Test Studio", Guid.NewGuid(), 10_000_000);
            typeof(StudioWorkspace).GetProperty(nameof(StudioWorkspace.Id))!.SetValue(workspace, WorkspaceId);
            Db.StudioWorkspaces.Add(workspace);

            var series = Series.Create(WorkspaceId, "Test Manga", ReadingDirection.RightToLeft);
            typeof(Series).GetProperty(nameof(Series.Id))!.SetValue(series, SeriesId);
            Db.Series.Add(series);

            Chapter = Chapter.Create(SeriesId, 1, "Chapter 1");
            Db.Chapters.Add(Chapter);
            Page = Page.CreateNew(Chapter.Id, 1, LayoutFormat.StandardPage, 1200, 1800, 300);
            Db.Pages.Add(Page);
            Panel = Panel.Create(Page.Id, 1, 1);
            Db.Panels.Add(Panel);

            var bible = SeriesBible.Create(SeriesId, WorkspaceId, "Series Bible");
            Db.SeriesBibles.Add(bible);
            Character = bible.AddEntry(BibleEntryCategory.Character, "HERO", "Hero Main", "The protagonist",
                referenceImageUrl: "data:image/png;base64,iVBORw0KGgo=");
            Character.AddRevision("v1", "{}", "hash-hero", effectiveFromChapterNumber: 1, isInitialVersion: true)
                .Lock(Guid.NewGuid());
            Db.SaveChanges();

            var line = ScriptLine.Create(Guid.NewGuid(), 1, "I will protect everyone!", null, Character.Id);
            line.AssignToPanel(Panel.Id);
            Db.ScriptLines.Add(line);

            var asset = Asset.Create(WorkspaceId, new string('b', 64), 1024, "image/png");
            Db.Assets.Add(asset);
            Db.AssetReferences.Add(AssetReference.Create(asset.Id, WorkspaceId, Chapter.Id, Page.Id, Guid.NewGuid(), 1, null,
                "panel1.png", Guid.NewGuid(), Panel.Id));
            Db.WorkspaceAiConfigs.Add(WorkspaceAiConfig.Create(WorkspaceId, "Google Gemini", "encrypted-workspace-key"));
            Db.SaveChanges();

            StorageMock.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream(new byte[] { 1, 2, 3 }));
            UsageServiceMock.Setup(u => u.CheckAvailabilityAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AiAvailability.Available());
            KeyProtectorMock.Setup(k => k.Unprotect("encrypted-workspace-key")).Returns("decrypted-workspace-key");
            AiClientMock.Setup(c => c.CompareImagesAsync(It.IsAny<VisionCompareRequest>(), It.IsAny<CancellationToken>()))
                .Callback((VisionCompareRequest r, CancellationToken _) => LastRequest = r)
                .ReturnsAsync(reply);
        }

        public ImageConsistencyService CreateService() => new(Db, StorageMock.Object, KeyProtectorMock.Object,
            UsageServiceMock.Object, AiClientMock.Object, Settings, Mock.Of<ILogger<ImageConsistencyService>>());

        public void VerifyNoAiCall() =>
            AiClientMock.Verify(c => c.CompareImagesAsync(It.IsAny<VisionCompareRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static VisionCompareResponse Reply(decimal? score, string? summary = null, params VisionDeviation[] devs) =>
        new(score is not null, score, devs, summary, "gemini-2.5-flash", 250, 60, DateTime.UtcNow, DateTime.UtcNow,
            score is null ? "INVALID_OUTPUT" : null, score is null ? "Kết quả vision không đọc được" : null);

    [Fact]
    public async Task ScoreBelowThreshold_CreatesWarningFinding_AndRecordsUsage()
    {
        var fixture = new TestFixture(Reply(0.62m, "Tóc và trang phục sai khác so với Character Sheet khóa v1",
            new VisionDeviation("hair", "Màu tóc bị đổi từ đen sang nâu sáng", "top-left"),
            new VisionDeviation("outfit", "Thiếu áo khoác choàng đặc trưng", "torso")));

        var result = await fixture.CreateService().CheckPanelConsistencyAsync(fixture.Panel.Id, threshold: 0.75m);

        result.Passed.Should().BeFalse();
        result.Score.Should().Be(0.62m);
        result.Deviations.Should().HaveCount(2);
        var finding = await fixture.Db.ConsistencyFindings.SingleAsync(f => f.Id == result.FindingId);
        finding.RuleCode.Should().Be(ConsistencyChecker.ImageInconsistency);
        finding.Severity.Should().Be(ConsistencyFindingSeverity.Warning, "vision chỉ cảnh báo, không chặn duyệt chương");
        finding.Evidence.Should().Contain("Màu tóc bị đổi");
        fixture.UsageServiceMock.Verify(u => u.RecordAsync(It.Is<AiUsageEntry>(e =>
            e.Status == AiUsageStatus.Succeeded && e.PromptTokens == 250 && e.Feature == "Consistency.ImageConsistencyCheck"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ScoreAboveThreshold_PassesWithoutFinding_UsingWorkspaceKeyThroughAiService()
    {
        var fixture = new TestFixture(Reply(0.92m, "Rất nhất quán"));

        var result = await fixture.CreateService().CheckPanelConsistencyAsync(fixture.Panel.Id);

        result.Passed.Should().BeTrue();
        fixture.Db.ConsistencyFindings.Should().BeEmpty();
        fixture.LastRequest!.ApiKey.Should().Be("decrypted-workspace-key");
        fixture.LastRequest.Provider.Should().Be(AiProtocol.ProviderGemini);
        fixture.LastRequest.CharacterName.Should().Be("Hero Main");
    }

    [Fact]
    public async Task WorkspaceWithoutKey_Throws_NoSystemFallbackKey()
    {
        var fixture = new TestFixture(Reply(0.9m));
        fixture.Db.WorkspaceAiConfigs.Remove(await fixture.Db.WorkspaceAiConfigs.SingleAsync());
        fixture.Db.SaveChanges();

        var act = () => fixture.CreateService().CheckPanelConsistencyAsync(fixture.Panel.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.VerifyNoAiCall();
    }

    [Fact]
    public async Task NoCharacterSheet_IsSkipped_WithoutCallingAi()
    {
        var fixture = new TestFixture(Reply(0.9m));
        fixture.Character.UpdateDetails(fixture.Character.Name, fixture.Character.Description, null, null, null,
            fixture.Character.Priority, fixture.Character.StrictCheck);
        fixture.Db.SaveChanges();

        var result = await fixture.CreateService().CheckPanelConsistencyAsync(fixture.Panel.Id);

        result.Skipped.Should().BeTrue();
        fixture.VerifyNoAiCall();
    }

    [Fact]
    public async Task UnreadableReply_Throws_InsteadOfPassing()
    {
        var fixture = new TestFixture(Reply(null));

        var act = () => fixture.CreateService().CheckPanelConsistencyAsync(fixture.Panel.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.Db.ConsistencyFindings.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AiUnavailable_Throws_BeforeCallingProvider()
    {
        var fixture = new TestFixture(Reply(0.9m));
        fixture.UsageServiceMock.Setup(u => u.CheckAvailabilityAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AiAvailability.Blocked(AiUsageStatus.Disabled, "AI tắt"));

        var act = () => fixture.CreateService().CheckPanelConsistencyAsync(fixture.Panel.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("AI tắt");
        fixture.VerifyNoAiCall();
    }

    [Fact]
    public async Task VisionAgent_ParsesModelJson_AndRejectsMissingScore()
    {
        var req = new VisionCompareRequest(AiProtocol.ProviderMock, "mock", null, "Lan",
            new ImageReference("sheet", "image/png", Convert.ToBase64String(new byte[] { 1, 2, 3 })),
            new ImageReference("art", "image/png", Convert.ToBase64String(new byte[] { 4, 5, 6 })));

        var ok = await new PanelForge.AI.Agents.VisionAgent(new PanelForge.AI.Demo.MockChatClient()).RunAsync(req, default);
        ok.Succeeded.Should().BeTrue(ok.ErrorMessage);
        ok.Score.Should().Be(0.9m);

        var chat = new Mock<IChatClient>();
        chat.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "{\"summaryReason\":\"thiếu điểm\"}")));
        var bad = await new PanelForge.AI.Agents.VisionAgent(chat.Object).RunAsync(req, default);
        bad.Succeeded.Should().BeFalse();
        bad.ErrorCode.Should().Be("INVALID_OUTPUT");
    }

    [Fact]
    public async Task PanelsController_CheckImageConsistency_ReturnsOkWithResult()
    {
        var mockService = new Mock<IImageConsistencyService>();
        var panelId = Guid.NewGuid();
        var expectedResult = new ImageConsistencyResultDto(
            PanelId: panelId,
            CharacterId: Guid.NewGuid(),
            CharacterName: "Akira",
            CharacterSheetVersion: 1,
            Score: 0.85m,
            Threshold: 0.75m,
            Passed: true,
            SummaryReason: "Khớp tốt",
            Deviations: Array.Empty<ImageConsistencyDeviationDto>()
        );
        mockService.Setup(s => s.CheckPanelConsistencyAsync(panelId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);
        var controller = new PanelsController(Mock.Of<MediatR.IMediator>());

        var actionResult = await controller.CheckImageConsistency(
            panelId, revisionRef: null, threshold: null, mockService.Object, CancellationToken.None);

        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeEquivalentTo(expectedResult);
    }
}
