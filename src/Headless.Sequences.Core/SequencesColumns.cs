// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Sql;
using Headless.UnitOfWork;

namespace Headless.Sequences;

#pragma warning restore CA2100

/// <summary>The counter table's columns, in each dialect's naming convention and quoted.</summary>
internal static class SequencesColumns
{
    public static string TenantId(ISqlDialect dialect) => _Column(dialect, "TenantId");

    public static string Name(ISqlDialect dialect) => _Column(dialect, "Name");

    // Quoted everywhere: PARTITION is a keyword in PostgreSQL's grammar.
    public static string Partition(ISqlDialect dialect) => _Column(dialect, "Partition");

    public static string Value(ISqlDialect dialect) => _Column(dialect, "Value");

    public static string CreatedAt(ISqlDialect dialect) => _Column(dialect, "CreatedAt");

    public static string UpdatedAt(ISqlDialect dialect) => _Column(dialect, "UpdatedAt");

    private static string _Column(ISqlDialect dialect, string pascalName) => dialect.Quote(dialect.Name(pascalName));
}
