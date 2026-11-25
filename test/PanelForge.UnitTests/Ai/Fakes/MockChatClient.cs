using Microsoft.Extensions.AI;
using PanelForge.AI.Demo.Fixtures;

namespace PanelForge.AI.Demo;

/// <summary>
/// IChatClient "Mock" cho demo: không gọi mạng, không cần API key, output xác định theo agent và lượt sửa
/// (xem <see cref="MockFixtures"/>). Không giả lập token usage: ChatResponse.Usage luôn null.
/// </summary>
public sealed class MockChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Tin nhắn người dùng cuối cùng luôn mở đầu bằng marker [[agent:...]] do agent dựng
        var user = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;
        var json = MockFixtures.Respond(user);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json)) { ModelId = "mock-fixture-v1" });
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("MockChatClient chỉ hỗ trợ GetResponseAsync.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}