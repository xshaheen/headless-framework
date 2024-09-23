// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Data;

namespace Framework.Database;

public interface ISqlConnectionFactory
{
    string GetConnectionString();

    ValueTask<IDbConnection> GetOpenConnectionAsync();

    ValueTask<IDbConnection> CreateNewConnectionAsync();
}
