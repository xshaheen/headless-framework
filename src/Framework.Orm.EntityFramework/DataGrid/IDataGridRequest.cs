// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;

namespace Framework.Orm.EntityFramework.DataGrid;

public interface IDataGridRequest : IHasMultiOrderByRequest
{
    public IndexPageRequest? Page { get; }
}

public abstract class DataGridRequest : IDataGridRequest
{
    public IndexPageRequest? Page { get; init; }

    public List<OrderBy>? Orders { get; init; }
}
