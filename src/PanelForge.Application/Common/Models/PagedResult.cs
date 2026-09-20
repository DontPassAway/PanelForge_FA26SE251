namespace PanelForge.Application.Common.Models;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int PageIndex,
    int PageSize,
    int TotalPages
);
