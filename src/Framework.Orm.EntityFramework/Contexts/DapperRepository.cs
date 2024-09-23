// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Data;
using Framework.Orm.Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Framework.Orm.EntityFramework.Contexts;

public sealed class DapperRepository(DbContext db) : IDapperRepository
{
    public ValueTask<IDbConnection> GetDbConnectionAsync()
    {
        var connection = db.Database.GetDbConnection();

        return ValueTask.FromResult<IDbConnection>(connection);
    }

    public ValueTask<IDbTransaction?> GetDbTransactionAsync()
    {
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction();

        return ValueTask.FromResult<IDbTransaction?>(transaction);
    }
}
