// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

public sealed class ContinuationPage<T>(IReadOnlyList<T> items, int size, string? continuationToken)
{
    public IReadOnlyList<T> Items { get; } = items;

    public int Size { get; } = size;

    public string? ContinuationToken { get; } = continuationToken;

    public bool HasNext => ContinuationToken is null;

    public ContinuationPage<TOutput> Select<TOutput>(Func<T, TOutput> map)
    {
        return new(Items.Select(map).ToList(), Size, ContinuationToken);
    }

    public ContinuationPage<T> Where(Func<T, bool> predicate)
    {
        return new(Items.Where(predicate).ToList(), Size, ContinuationToken);
    }
}
