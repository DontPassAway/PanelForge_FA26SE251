using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using PanelForge.AI.Agents;
using PanelForge.AI.Gateway;

namespace PanelForge.UnitTests.Ai;

/// <summary>Bước nhẹ (Supervisor/Copilot) phải gửi reasoning_effort và trần token thấp; bước thường giữ nguyên.</summary>
public class AgentRunnerReasoningTests
{
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public JsonDocument? Body;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            const string json = """
                {"id":"x","object":"chat.completion","created":0,"model":"m",
                 "choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"{\"ok\":true}"}}]}
                """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }

    private static async Task<JsonElement> SendAsync(string model, bool light, bool lowReasoning = false)
    {
        var handler = new CaptureHandler();
        var client = new OpenAIClient(new ApiKeyCredential("test"), new OpenAIClientOptions
        {
            Endpoint = new Uri("https://example.test/v1/"),
            Transport = new HttpClientPipelineTransport(new HttpClient(handler))
        });
        IChatClient chat = client.GetChatClient(model).AsIChatClient();
        var runner = new AgentRunner(chat, Options.Create(new ProviderOptions()), model);
        var prompt = new PromptInfo("t", "sys", "t@1", "ref");

        var call = await runner.RunAsync<string>(prompt, "hi", t => (t, []), CancellationToken.None, light: light, lowReasoning: lowReasoning);

        Assert.True(call.Succeeded);
        return handler.Body!.RootElement;
    }

    private static int Cap(JsonElement body) =>
        (body.TryGetProperty("max_completion_tokens", out var c) ? c : body.GetProperty("max_tokens")).GetInt32();

    [Theory]
    [InlineData("gemini-2.5-flash", "none")]
    [InlineData("gemini-2.5-pro", "low")]
    public async Task Light_step_sends_reasoning_effort_and_low_cap(string model, string effort)
    {
        var body = await SendAsync(model, light: true);

        Assert.Equal(effort, body.GetProperty("reasoning_effort").GetString());
        Assert.Equal(AgentRunner.LightMaxOutputTokens, Cap(body));
    }

    [Fact]
    public async Task Low_reasoning_step_sends_low_and_keeps_full_cap()
    {
        var body = await SendAsync("gemini-2.5-flash", light: false, lowReasoning: true);

        Assert.Equal("low", body.GetProperty("reasoning_effort").GetString());
        Assert.Equal(new ProviderOptions().MaxOutputTokens, Cap(body));
    }

    [Fact]
    public async Task Normal_step_keeps_default_thinking_and_cap()
    {
        var body = await SendAsync("gemini-2.5-flash", light: false);

        Assert.False(body.TryGetProperty("reasoning_effort", out _));
        Assert.Equal(new ProviderOptions().MaxOutputTokens, Cap(body));
    }
}
