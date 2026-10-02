using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using PanelForge.AI.Contracts;

namespace PanelForge.AI.Workflows;

public sealed class WorkflowOptions
{
    public const string SectionName = "Workflow";
    /// <summary>Số job tối đa đang chờ; đầy → từ chối (429) thay vì ngốn bộ nhớ.</summary>
    public int QueueCapacity { get; set; } = 8;
    /// <summary>Thời gian tối đa cho cả một run.</summary>
    public int RunTimeoutSeconds { get; set; } = 300;
}

public enum EnqueueResult { Accepted, Duplicate, Full }

/// <summary>Hàng đợi giới hạn dung lượng, một người đọc (RunWorker). Job chỉ nằm trong bộ nhớ: restart là mất → Backend đánh dấu Interrupted.</summary>
public sealed class RunQueue
{
    private readonly Channel<AiRunJob> _channel;
    private readonly ConcurrentDictionary<Guid, byte> _known = new();

    public RunQueue(IOptions<WorkflowOptions> options)
    {
        _channel = Channel.CreateBounded<AiRunJob>(new BoundedChannelOptions(Math.Max(1, options.Value.QueueCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });
    }

    public ChannelReader<AiRunJob> Reader => _channel.Reader;
    public int Depth => _channel.Reader.Count;

    /// <summary>Idempotent theo RunId: Backend retry giao job không chạy hai lần.</summary>
    public EnqueueResult TryEnqueue(AiRunJob job)
    {
        if (!_known.TryAdd(job.RunId, 0)) return EnqueueResult.Duplicate;
        if (_channel.Writer.TryWrite(job)) return EnqueueResult.Accepted;
        _known.TryRemove(job.RunId, out _);
        return EnqueueResult.Full;
    }

    public void Forget(Guid runId) => _known.TryRemove(runId, out _);
}