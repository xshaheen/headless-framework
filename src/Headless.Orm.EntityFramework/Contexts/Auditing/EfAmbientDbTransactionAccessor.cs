// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.AuditLog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>
/// EF Core implementation of <see cref="IAmbientDbTransactionAccessor"/>. Returns the
/// <see cref="DbConnection"/> and <see cref="DbTransaction"/> backing the
/// <see cref="DbContext"/>'s current <see cref="IDbContextTransaction"/>, when one is active.
/// </summary>
/// <remarks>
/// Registered as singleton from <c>AddHeadlessDbContextServices</c> so any audit storage
/// implementation can opt into transaction enrollment without depending on EF Core.
/// </remarks>
internal sealed class EfAmbientDbTransactionAccessor : IAmbientDbTransactionAccessor
{
    public (DbConnection? Connection, DbTransaction? Transaction) TryResolve(object savingContext)
    {
        if (savingContext is not DbContext context)
        {
            return (null, null);
        }

        var currentTransaction = context.Database.CurrentTransaction;

        if (currentTransaction is null)
        {
            return (null, null);
        }

        // GetDbTransaction is a Relational extension and returns the active provider transaction.
        return (context.Database.GetDbConnection(), currentTransaction.GetDbTransaction());
    }
}
