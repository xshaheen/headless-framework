// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.EntityFramework.Internal;

internal static class SqlServerScopeCollation
{
    private const string _ProviderName = "Microsoft.EntityFrameworkCore.SqlServer";
    private const string _Collation = "Latin1_General_100_BIN2";

    internal static void Configure(PropertyBuilder<string?> property, string? providerName)
    {
        if (providerName is _ProviderName)
        {
            // The scope column and its indexes must both distinguish case and accents under a non-binary database default.
            property.UseCollation(_Collation);
        }
    }

    internal static void EnsureConfigured(
        DbContext context,
        Type entityType,
        string propertyName,
        string configureMethod
    )
    {
        if (context.Database.ProviderName is not _ProviderName)
        {
            return;
        }

        // EF omits collations from the runtime model. The design-time model supplies the actual column mappings.
        var model = context.GetService<IDesignTimeModel>().Model;
        var columns = model.FindEntityType(entityType)?.FindProperty(propertyName)?.GetTableColumnMappings().ToArray();
        var defaultCollation = model.GetCollation();

        if (
            columns is { Length: > 0 }
            && columns.All(mapping =>
                string.Equals(mapping.Column.Collation ?? defaultCollation, _Collation, StringComparison.Ordinal)
            )
        )
        {
            return;
        }

        throw new InvalidOperationException(
            $"The SQL Server model for `{context.GetType().FullName}` requires collation `{_Collation}` on `{entityType.Name}.{propertyName}`. "
                + $"Call `modelBuilder.{configureMethod}(this)` in `OnModelCreating` or apply `UseCollation(\"{_Collation}\")` to the property."
        );
    }
}
