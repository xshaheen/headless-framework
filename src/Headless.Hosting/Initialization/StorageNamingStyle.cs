// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using System.Text.Json;
using Headless.Checks;

namespace Headless.Hosting.Initialization;

/// <summary>The identifier casing a database's conventions call for.</summary>
[PublicAPI]
public enum StorageNamingStyle
{
    /// <summary>PascalCase tables, columns, and constraint names (<c>FeatureValues</c>, <c>PK_FeatureValues</c>): SQL Server's convention.</summary>
    PascalCase = 0,

    /// <summary>
    /// Lower-case snake_case names (<c>feature_values</c>, <c>pk_feature_values</c>): PostgreSQL's convention, which
    /// folds unquoted identifiers to lower case, so the names stay usable in hand-written SQL without quotes.
    /// </summary>
    SnakeCase = 1,
}
