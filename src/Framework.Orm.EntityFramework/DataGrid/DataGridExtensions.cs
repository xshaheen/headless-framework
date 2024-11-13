// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.Checks;
using Framework.Orm.EntityFramework.DataGrid.Ordering;
using Framework.Orm.EntityFramework.DataGrid.Pagination;

namespace Framework.Orm.EntityFramework.DataGrid;

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

        return source.Order(request.Orders).ToIndexPageAsync(request, cancellationToken);
    }
}
