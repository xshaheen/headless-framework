// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Orm.EntityFramework.DataGrid.Pagination;

public sealed class IndexPage<T>
{
    public IndexPage(IReadOnlyList<T> items, int index, int size, int totalItems)
    {
        Items = items;
        Index = index;
        Size = size;
        TotalItems = totalItems;
        TotalPages = TotalItems == 0 || Size == 0 ? 0 : (int)Math.Ceiling(TotalItems / (decimal)Size);
    }

    public IReadOnlyList<T> Items { get; }

    public int Index { get; }

    public int Size { get; }

    public int TotalItems { get; }

    public int TotalPages { get; }

    public bool HasPrevious => Index > 0;

    public bool HasNext => Index < TotalPages - 1;

    public IndexPage<TOutput> GetProjectedPage<TOutput>(Func<T, TOutput> map) =>
        new(Items.Select(map).ToList(), Index, Size, TotalItems);
}
