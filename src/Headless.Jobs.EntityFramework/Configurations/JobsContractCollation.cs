// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Configurations;

// The ordinal collation every durable Jobs identity column is stored under: function names, contract versions,
// keyed-scheduling keys, and idempotency keys. Binary comparison is required because a case-insensitive database
// default would merge two caller-distinct identities into one row. One mapping serves both the model
// configurations that emit the collation and the runtime checks that verify the schema was generated from it.
internal static class JobsContractCollation
{
    internal static string? TryResolve(string? providerName) =>
        providerName switch
        {
            "Microsoft.EntityFrameworkCore.SqlServer" => "Latin1_General_100_BIN2",
            "Npgsql.EntityFrameworkCore.PostgreSQL" => "C",
            _ => null,
        };

    internal static string Require(string? providerName, string unsupportedProviderMessage) =>
        TryResolve(providerName) ?? throw new NotSupportedException(unsupportedProviderMessage);
}
