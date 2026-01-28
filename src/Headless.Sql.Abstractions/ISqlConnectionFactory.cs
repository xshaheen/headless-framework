// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.Sql;

[PublicAPI]
public interface ISqlConnectionFactory
{
    string GetConnectionString();

    ValueTask<DbConnection> CreateNewConnectionAsync(CancellationToken cancellationToken = default);
}
