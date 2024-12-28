// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Framework.Database;

[PublicAPI]
public interface ISqlConnectionFactory : IAsyncDisposable
{
    string GetConnectionString();

    ValueTask<DbConnection> GetOpenConnectionAsync();

    ValueTask<DbConnection> CreateNewConnectionAsync();
}
