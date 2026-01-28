// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;

namespace Headless.Orm.EntityFramework.DataGrid;

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
