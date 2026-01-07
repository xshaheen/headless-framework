// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Framework.Sql;

[PublicAPI]
public interface ISqlConnectionFactory
{
    string GetConnectionString();

    ValueTask<DbConnection> CreateNewConnectionAsync(CancellationToken cancellationToken = default);
}
