using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using PanelForge.AI.Demo;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence.Seeders;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>V1.2 (CF1): story arc của Series và outline của Chapter là đầu vào của Writer Agent; null = giữ nguyên, rỗng = xoá.</summary>
public class StoryArcOutlineTests
{
    private static string Ask(IChatClient mock, string msg) =>
        mock.GetResponseAsync([new ChatMessage(ChatRole.User, msg)]).Result.Text;

    private static async Task<string> FirstWriterPromptAsync(AiHarness h)
    {
        var runId = await h.StartRunAsync();
        var mock = new MockChatClient();
        var chat = new ScriptedChatClient((_, msg) => Ask(mock, msg));
        await h.ExecuteWorkflowAsync(runId, chat);
        return chat.UserMessages.First(m => m.Contains("[[agent:writer]]"));
    }

    [Fact]
    public async Task WriterPrompt_IncludesStoryArcAndChapterOutline_WhenSet()
    {
        var h = await AiHarness.CreateAsync();
        var series = await h.Db.Series.SingleAsync(s => s.Id == AiDemoSeeder.SeriesId);
        var chapter = await h.Db.Chapters.SingleAsync(c => c.Id == AiDemoSeeder.ChapterId);
        series.UpdateDetails(series.Title, series.Synopsis, series.ReadingDirection, storyArc: "Arc 1: An tìm lại tiệm đồng hồ của cha.");
        chapter.UpdateDetails(chapter.Title, chapter.TargetReleaseDate, "Chương mở đầu: bà Lan xuất hiện lúc nửa đêm.");
        await h.Db.SaveChangesAsync();

        var prompt = await FirstWriterPromptAsync(h);

        prompt.Should().Contain("Story arc của Series").And.Contain("An tìm lại tiệm đồng hồ của cha");
        prompt.Should().Contain("Outline chương").And.Contain("bà Lan xuất hiện lúc nửa đêm");
    }

    [Fact]
    public async Task WriterPrompt_OmitsStoryArcAndOutlineSections_WhenNotSet()
    {
        var h = await AiHarness.CreateAsync();

        var prompt = await FirstWriterPromptAsync(h);

        prompt.Should().NotContain("Story arc của Series").And.NotContain("Outline chương");
    }

    [Fact]
    public void Series_StoryArc_NullKeeps_EmptyClears_ValueIsTrimmed()
    {
        var series = Series.Create(Guid.NewGuid(), "Manga", ReadingDirection.RightToLeft);

        series.UpdateDetails("Manga", null, ReadingDirection.RightToLeft, storyArc: "  Arc 1  ");
        series.StoryArc.Should().Be("Arc 1");

        series.UpdateDetails("Manga", null, ReadingDirection.RightToLeft);
        series.StoryArc.Should().Be("Arc 1");

        series.UpdateDetails("Manga", null, ReadingDirection.RightToLeft, storyArc: "  ");
        series.StoryArc.Should().BeNull();
    }

    [Fact]
    public void Chapter_Outline_NullKeeps_EmptyClears_ValueIsTrimmed()
    {
        var chapter = Chapter.Create(Guid.NewGuid(), 1, "Ch 1");

        chapter.UpdateDetails("Ch 1", null, "  Mở đầu  ");
        chapter.Outline.Should().Be("Mở đầu");

        chapter.UpdateDetails("Ch 1 (sửa)", null);
        chapter.Outline.Should().Be("Mở đầu");

        chapter.UpdateDetails("Ch 1", null, "");
        chapter.Outline.Should().BeNull();
    }
}
