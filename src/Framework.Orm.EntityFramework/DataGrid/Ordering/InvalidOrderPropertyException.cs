// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Orm.EntityFramework.DataGrid.Ordering;

public sealed class InvalidOrderPropertyException(string message, Exception? innerException)
    : Exception(message, innerException);
