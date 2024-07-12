using Framework.Arguments;
using Framework.Database.EntityFramework.DataGrid.Ordering;
using Framework.Database.EntityFramework.DataGrid.Pagination;

namespace Framework.Database.EntityFramework.DataGrid;

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
