using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using PanelForge.AI.Agents;
using PanelForge.AI.Contracts;
using PanelForge.AI.Gateway;
using PanelForge.Application.Common;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.AiAgents.Models;
using Xunit;

namespace PanelForge.UnitTests.Ai;

/// <summary>Rút gọn thoại được sửa tối đa hai lần; lỗi của model không bị báo như lỗi nhập liệu.</summary>
public sealed class CopilotShortenRepairTests
{
    private const string Original = "Cháu đã kiểm tra rất kỹ rồi nhưng chiếc tủ cũ vẫn mở toang";

    private sealed class ScriptedChat(params string[] texts) : IChatClient
    {
        public List<string> Prompts { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Prompts.Add(string.Join("\n", messages.Select(m => m.Text)));
            var text = texts[Math.Min(Prompts.Count - 1, texts.Length - 1)];
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $$"""{"text":"{{text}}","reason":"ngắn hơn"}""")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static Task<CopilotResponse> RunAsync(ScriptedChat chat, string task = CopilotTasks.LettererShorten, int? maxChars = 20) =>
        new CopilotAgent(new AgentRunner(chat, Options.Create(new ProviderOptions()))).RunAsync(
            new CopilotRequest(task, AiProtocol.ProviderMock, "m", null,
                new CopilotLine(Guid.NewGuid(), null, Original, null, []), [], maxChars, null), default);

    [Fact]
    public async Task Shorten_SucceedsOnSecondRepair_AndToldHowMuchToCut()
    {
        var chat = new ScriptedChat(new string('a', 25), new string('b', 22), "Tủ cũ vẫn mở toang");

        var result = await RunAsync(chat);

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        result.RepairAttempts.Should().Be(2);
        chat.Prompts[0].Should().Contain("aimChars=16");
        chat.Prompts[1].Should().Contain("cần bớt ít nhất 5 ký tự");
    }

    [Fact]
    public async Task Shorten_StopsAfterTwoRepairs_WithModelErrorCode()
    {
        var chat = new ScriptedChat(new string('a', 25));

        var result = await RunAsync(chat);

        chat.Prompts.Should().HaveCount(3);
        result.Succeeded.Should().BeFalse();
        CopilotGate.FailureCode(result).Should().Be(ResultErrorCodes.AiInvalidOutput);
    }

    [Fact]
    public async Task Rewrite_KeepsSingleRepair()
    {
        var chat = new ScriptedChat(Original);
        (await RunAsync(chat, CopilotTasks.WriterRewrite, null)).Succeeded.Should().BeFalse();
        chat.Prompts.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("PROVIDER_UNAVAILABLE")]
    [InlineData("PROVIDER_ERROR")]
    [InlineData("RUN_TIMEOUT")]
    public void ProviderFailures_AreReportedAsAiUnavailable(string code) =>
        CopilotGate.FailureCode(new CopilotResponse(false, null, null, "p", "r", null, null, 0, DateTime.UtcNow, DateTime.UtcNow, code, "x"))
            .Should().Be(ResultErrorCodes.AiUnavailable);

    [Fact]
    public void CopilotTimeout_LeavesRoomForRepairs() => new AiAgentSettings().CopilotTimeoutSeconds.Should().Be(30);
}
