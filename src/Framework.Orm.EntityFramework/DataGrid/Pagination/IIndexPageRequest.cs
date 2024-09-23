// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Orm.EntityFramework.DataGrid.Pagination;

public interface IIndexPageRequest
{
    public int Index { get; }

    public int Size { get; }
}

public abstract class IndexPageRequest : IIndexPageRequest
{
    public int Index { get; init; }

    public int Size { get; init; } = 25;
}
