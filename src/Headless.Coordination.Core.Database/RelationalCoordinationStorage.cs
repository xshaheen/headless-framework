// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using FluentValidation;
using Headless.Hosting.Initialization.Schema;
using Headless.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Coordination;

/// <summary>What the one relational membership store runs against.</summary>
/// <param name="Dialect">The engine's dialect.</param>
/// <param name="CreateConnection">Creates an unopened connection to the database that holds the tables.</param>
/// <param name="CommandTimeoutSeconds">The timeout of every command.</param>
/// <param name="Tables">The membership tables, named by the dialect in the configured schema.</param>
internal sealed record RelationalCoordinationStorage(
    ISqlDialect Dialect,
    Func<DbConnection> CreateConnection,
    int CommandTimeoutSeconds,
    CoordinationTables Tables
);
