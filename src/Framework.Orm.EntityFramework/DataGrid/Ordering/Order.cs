// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Orm.EntityFramework.DataGrid.Ordering;

public sealed record Order(string Property, bool Ascending = false);

public sealed class Orders : List<Order>;
