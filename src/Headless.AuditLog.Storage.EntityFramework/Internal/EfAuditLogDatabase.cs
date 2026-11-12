// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Headless.AuditLog.Internal;

/// <summary>
/// The database an Entity Framework audit storage registration was made for: <see langword="null"/> for the
/// provider-neutral <c>UseEntityFramework</c>, and a value for a database-specific package's registration. The
/// startup validator compares it with the database the context really runs on.
/// </summary>
internal sealed record EfAuditLogDatabase(StorageProvider? Registered)
{
    public const string SqlServerEfProviderName = "Microsoft.EntityFrameworkCore.SqlServer";

    /// <summary>The database behind <paramref name="context"/>, when a Headless audit package exists for it.</summary>
    public static StorageProvider? Provider(DbContext context) => Provider(context.Database.ProviderName);

    /// <summary>The database an EF Core provider name stands for, when a Headless audit package exists for it.</summary>
    public static StorageProvider? Provider(string? providerName)
    {
        return providerName switch
        {
            HeadlessStorageNaming.NpgsqlEfProviderName => StorageProvider.PostgreSql,
            SqlServerEfProviderName => StorageProvider.SqlServer,
            _ => null,
        };
    }

    /// <summary>The registration method of the Entity Framework audit package for <paramref name="provider"/>.</summary>
    public static string RegistrationOf(StorageProvider? provider)
    {
        return provider switch
        {
            StorageProvider.PostgreSql =>
                "UsePostgreSqlEntityFramework<TContext>() from Headless.AuditLog.Storage.PostgreSql.EntityFramework",
            StorageProvider.SqlServer =>
                "UseSqlServerEntityFramework<TContext>() from Headless.AuditLog.Storage.SqlServer.EntityFramework",
            _ => "UseEntityFramework<TContext>() from Headless.AuditLog.Storage.EntityFramework",
        };
    }
}
