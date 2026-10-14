using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using PanelForge.API.Controllers;
using PanelForge.Application.Common.Interfaces.Authentication;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Common.Models;
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

public class ImageConsistencyServiceTests
{
    private sealed class TestFixture
    {
        public readonly PanelForgeDbContext Db = InMemoryDb.Create();
        public readonly Mock<IAssetStorage> StorageMock = new();
        public readonly Mock<IAiKeyProtector> KeyProtectorMock = new();
        public readonly Mock<IAiUsageService> UsageServiceMock = new();
        public readonly Mock<HttpMessageHandler> HttpHandlerMock = new();
        public readonly AiVisionSettings VisionSettings;
        public readonly Guid WorkspaceId = Guid.NewGuid();
        public readonly Guid SeriesId = Guid.NewGuid();
        public readonly Chapter Chapter;
        public readonly Page Page;
        public readonly Panel Panel;
        public readonly BibleEntry Character;

        public TestFixture(string aiResponseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            VisionSettings = new AiVisionSettings
            {
                Provider = "Gemini",
                ApiKey = "fallback-test-api-key",
                ModelName = "gemini-2.5-flash",
                EndpointUrl = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
                SimilarityThreshold = 0.75m
            };

            // Setup Series, Workspace, Chapter, Page, Panel
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

            // Series Bible with Locked Character
            var bible = SeriesBible.Create(SeriesId, WorkspaceId, "Series Bible");
            Db.SeriesBibles.Add(bible);

            Character = bible.AddEntry(BibleEntryCategory.Character, "HERO", "Hero Main", "The protagonist");
            Character.AddRevision("v1", "{}", "hash-hero", effectiveFromChapterNumber: 1, isInitialVersion: true)
                .Lock(Guid.NewGuid());

            Db.SaveChanges();

            // Link ScriptLine to Panel
            var line = ScriptLine.Create(Guid.NewGuid(), 1, "I will protect everyone!", null, Character.Id, Panel.Id);
            Db.ScriptLines.Add(line);

            // Create Asset & AssetReference for Panel
            var sha = new string('b', 64);
            var asset = Asset.Create(WorkspaceId, sha, 1024, "image/png");
            Db.Assets.Add(asset);

            var artRef = AssetReference.Create(asset.Id, WorkspaceId, Chapter.Id, Page.Id, Guid.NewGuid(), 1, null, "panel1.png", Guid.NewGuid(), Panel.Id);
            Db.AssetReferences.Add(artRef);

            Db.SaveChanges();

            // Setup Storage mock
            StorageMock.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MemoryStream(new byte[] { 1, 2, 3 }));

            // Setup KeyProtector mock
            KeyProtectorMock.Setup(k => k.Unprotect("encrypted-workspace-key"))
                .Returns("decrypted-workspace-key");

            // Setup HTTP mock
            var httpResponse = new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(aiResponseContent, Encoding.UTF8, "application/json")
            };

            HttpHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(httpResponse);
        }

        public ImageConsistencyService CreateService()
        {
            var httpClient = new HttpClient(HttpHandlerMock.Object);
            var options = Options.Create(VisionSettings);
            var logger = Mock.Of<ILogger<ImageConsistencyService>>();

            return new ImageConsistencyService(
                httpClient,
                Db,
                StorageMock.Object,
                KeyProtectorMock.Object,
                UsageServiceMock.Object,
                options,
                logger
            );
        }
    }

    [Fact]
    public async Task CompareImagesAsync_WhenScoreBelowThreshold_ShouldFlagAndCreateConsistencyFinding()
    {
        // Arrange
        var aiChatResponse = new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        role = "assistant",
                        content = JsonSerializer.Serialize(new
                        {
                            score = 0.62,
                            deviations = new[]
                            {
                                new { aspect = "hair", description = "Màu tóc bị đổi từ đen sang nâu sáng", regionHint = "top-left" },
                                new { aspect = "outfit", description = "Thiếu áo khoác choàng đặc trưng", regionHint = "torso" }
                            },
                            summaryReason = "Tóc và trang phục sai khác so với Character Sheet khóa v1"
                        })
                    }
                }
            },
            usage = new { prompt_tokens = 250, completion_tokens = 60 }
        };

        var fixture = new TestFixture(JsonSerializer.Serialize(aiChatResponse));
        var service = fixture.CreateService();

        // Act
        var result = await service.CheckPanelConsistencyAsync(fixture.Panel.Id, threshold: 0.75m);

        // Assert
        result.Should().NotBeNull();
        result.Passed.Should().BeFalse();
        result.Score.Should().Be(0.62m);
        result.Deviations.Should().HaveCount(2);
        result.Deviations[0].Aspect.Should().Be("hair");
        result.FindingId.Should().NotBeNull();

        // Kiểm tra bản ghi ConsistencyFinding được tạo trong DB với mã luật IMAGE_INCONSISTENCY
        var finding = fixture.Db.ConsistencyFindings.FirstOrDefault(f => f.Id == result.FindingId);
        finding.Should().NotBeNull();
        finding!.RuleCode.Should().Be(ConsistencyChecker.ImageInconsistency);
        finding.Status.Should().Be(ConsistencyFindingStatus.Open);
        finding.Message.Should().Contain("Tóc và trang phục sai khác");
        finding.Evidence.Should().Contain("Màu tóc bị đổi");

        // Kiểm tra AI Provenance ghi nhận thành công
        fixture.UsageServiceMock.Verify(u => u.RecordAsync(
            It.Is<AiUsageEntry>(e =>
                e.Status == AiUsageStatus.Succeeded &&
                e.PromptTokens == 250 &&
                e.CompletionTokens == 60 &&
                e.Feature == "Consistency.ImageConsistencyCheck"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompareImagesAsync_WhenScoreAboveThreshold_ShouldPassWithoutFinding()
    {
        // Arrange
        var aiChatResponse = new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        role = "assistant",
                        content = JsonSerializer.Serialize(new
                        {
                            score = 0.92,
                            deviations = Array.Empty<object>(),
                            summaryReason = "Rất nhất quán với Character Sheet"
                        })
                    }
                }
            },
            usage = new { prompt_tokens = 200, completion_tokens = 30 }
        };

        var fixture = new TestFixture(JsonSerializer.Serialize(aiChatResponse));
        var service = fixture.CreateService();

        // Act
        var result = await service.CheckPanelConsistencyAsync(fixture.Panel.Id, threshold: 0.75m);

        // Assert
        result.Passed.Should().BeTrue();
        result.Score.Should().Be(0.92m);
        result.Deviations.Should().BeEmpty();
        result.FindingId.Should().BeNull();

        // Không tạo finding khi đạt
        fixture.Db.ConsistencyFindings.Should().BeEmpty();

        // AI Provenance vẫn được lưu
        fixture.UsageServiceMock.Verify(u => u.RecordAsync(
            It.Is<AiUsageEntry>(e => e.Status == AiUsageStatus.Succeeded),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveApiKey_PrefersWorkspaceKeyOverFallbackSettings()
    {
        // Arrange
        var aiChatResponse = new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        role = "assistant",
                        content = "{\"score\": 0.88, \"deviations\": [], \"summaryReason\": \"OK\"}"
                    }
                }
            },
            usage = new { prompt_tokens = 100, completion_tokens = 20 }
        };

        var fixture = new TestFixture(JsonSerializer.Serialize(aiChatResponse));

        // Thêm WorkspaceAiConfig vào DB với key đã mã hóa
        var wsConfig = WorkspaceAiConfig.Create(fixture.WorkspaceId, "Google Gemini", "encrypted-workspace-key");
        fixture.Db.WorkspaceAiConfigs.Add(wsConfig);
        fixture.Db.SaveChanges();

        var service = fixture.CreateService();

        // Act
        var result = await service.CheckPanelConsistencyAsync(fixture.Panel.Id);

        // Assert
        result.Passed.Should().BeTrue();

        // Verify that HTTP request header had the decrypted workspace key
        fixture.HttpHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Headers.Authorization != null &&
                req.Headers.Authorization.Parameter == "decrypted-workspace-key"),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task PanelsController_CheckImageConsistency_ReturnsOkWithResult()
    {
        // Arrange
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

        // Act
        var actionResult = await controller.CheckImageConsistency(
            panelId, revisionRef: null, threshold: null, mockService.Object, CancellationToken.None);

        // Assert
        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeEquivalentTo(expectedResult);
    }
}
