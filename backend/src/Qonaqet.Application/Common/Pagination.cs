namespace Qonaqet.Application.Common;

/// <summary>Səhifələnmiş cavab (<c>CLAUDE.md</c> → API conventions: <c>{ items, total }</c>).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, long Total);

/// <summary><c>?page=&amp;pageSize=</c> qaydası: səhifə 1-dən başlayır, ölçü ən çox 50, default 20.</summary>
public readonly record struct PageRequest(int Page, int PageSize)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public int Skip => (Page - 1) * PageSize;

    /// <summary>Sərhəddən kənar dəyərləri düzəldir (səhv yox, normallaşdırma).</summary>
    public static PageRequest Of(int? page, int? pageSize) =>
        new(Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
}
