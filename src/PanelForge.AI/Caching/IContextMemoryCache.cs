using PanelForge.AI.Agents;
using PanelForge.AI.Contracts;

namespace PanelForge.AI.Caching;

public sealed record CachedBibleContext(
    string FormattedBibleBlock,
    int OriginalChars,
    int CondensedChars,
    int EstimatedTokensSaved,
    bool IsCacheHit);

public interface IContextMemoryCache
{
    CachedBibleContext GetOrBuildBibleSummary(AgentContext ctx, KeyMaps keys);
}
