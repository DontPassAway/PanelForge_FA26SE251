using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using PanelForge.AI.Agents;
using PanelForge.AI.Contracts;
using PanelForge.AI.Gateway;

namespace PanelForge.UnitTests.Ai;

/// <summary>
/// Lỗi thật trên gemini-2.5-flash: model gắn thẻ tên nhân vật cho dòng chỉ có đại từ ("Cậu nhìn...").
/// Như Backend, thẻ không khớp chữ trong dòng bị bỏ; bản nháp vẫn hợp lệ, thẻ khớp vẫn giữ.
/// </summary>
public class WriterRefDropTests
{
    private sealed class OneReply(string text) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task Ref_not_in_line_text_is_dropped_instead_of_failing_draft()
    {
        var kha = new BibleEntryContext(Guid.NewGuid(), "KHA_VU", "Character", "Kha Vũ", "Bạn của Lâm Dư.", Guid.NewGuid(), 1, 1, null);
        var ctx = new AgentContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, Guid.NewGuid(), "H", null, "b", "LeftToRight",
            0, "Gemini", "gemini-2.5-flash", 2, [kha], [kha.RevisionId], []);
        const string output = """
            {"lines":[{"kind":"Action","text":"Cậu nhìn Kha Vũ, lòng trống rỗng.",
              "refs":[{"newName":"Lâm Dư","category":"Character","text":"Lâm Dư"},{"code":"KHA_VU","text":"Kha Vũ"}]}],
             "revisionNotes":null}
            """;
        var runner = new AgentRunner(new OneReply(output), Options.Create(new ProviderOptions()), "gemini-2.5-flash");

        var (call, _) = await new WriterAgent(runner).RunAsync(ctx, new KeyMaps(ctx), 0, null, null, CancellationToken.None);

        call.Succeeded.Should().BeTrue(call.ErrorMessage);
        var refs = call.Payload!.Lines.Single().Refs!;
        refs.Should().ContainSingle().Which.BibleEntryId.Should().Be(kha.EntryId);
    }

    [Fact]
    public async Task Unknown_bible_code_becomes_new_entry_ref_instead_of_failing_draft()
    {
        // Lỗi thật trên llama-3.3-70b (Cloudflare): model tự đặt code LOC_GATE / PROP_SWORD không có trong Bible
        var akira = new BibleEntryContext(Guid.NewGuid(), "CHR_AKIRA", "Character", "AKIRA", "Kiếm sĩ.", Guid.NewGuid(), 1, 1, null);
        var ctx = new AgentContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, Guid.NewGuid(), "H", null, "b", "LeftToRight",
            0, "Cloudflare", "@cf/meta/llama-3.3-70b-instruct-fp8-fast", 2, [akira], [akira.RevisionId], []);
        const string output = """
            {"lines":[{"kind":"Action","text":"AKIRA rút thanh kiếm phát sáng trước cổng làng.",
              "refs":[{"code":"CHR_AKIRA","text":"AKIRA"},{"code":"PROP_SWORD","text":"thanh kiếm phát sáng"},
                      {"code":"LOC_GATE","text":"cổng làng"},{"code":"XYZ_1","text":"rút"}]}],
             "revisionNotes":null}
            """;
        var runner = new AgentRunner(new OneReply(output), Options.Create(new ProviderOptions()), "llama");

        var (call, _) = await new WriterAgent(runner).RunAsync(ctx, new KeyMaps(ctx), 0, null, null, CancellationToken.None);

        call.Succeeded.Should().BeTrue(call.ErrorMessage);
        var refs = call.Payload!.Lines.Single().Refs!;
        refs.Should().HaveCount(3, "mã không đoán được loại (XYZ_1) bị bỏ, không làm hỏng bản nháp");
        refs.Should().Contain(r => r.BibleEntryId == akira.EntryId);
        refs.Should().Contain(r => r.BibleEntryId == null && r.NewName == "thanh kiếm phát sáng" && r.NewCategory == "Prop");
        refs.Should().Contain(r => r.BibleEntryId == null && r.NewName == "cổng làng" && r.NewCategory == "Location");
    }
}
