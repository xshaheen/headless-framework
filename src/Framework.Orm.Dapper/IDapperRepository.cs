// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;

namespace Framework.Orm.Dapper;

public interface IDapperRepository
{
    ValueTask<IDbConnection> GetDbConnectionAsync();

    ValueTask<IDbTransaction?> GetDbTransactionAsync();
}
