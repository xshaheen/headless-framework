// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.Primitives;

public interface IDataGridRequest : IHasMultiOrderByRequest
{
    IndexPageRequest? Page { get; }
}

public abstract class DataGridRequest : IDataGridRequest
{
    public IndexPageRequest? Page { get; init; }

    public List<OrderBy>? Orders { get; init; }
}

[PublicAPI]
public static class DataGridExtensions
{
    public static ValueTask<IndexPage<T>> ToDataGridAsync<T>(
        this IQueryable<T> source,
        IDataGridRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(source);
        Argument.IsNotNull(request);

        var query = source;

        if (request.Orders is { Count: > 0 })
        {
            query = source.OrderBy(request.Orders);
        }

        return query.ToIndexPageAsync(request.Page, cancellationToken);
    }
}
