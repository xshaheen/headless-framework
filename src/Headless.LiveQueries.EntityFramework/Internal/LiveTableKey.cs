// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries.Internal;

/// <summary>Builds the dependency key of a mapped table or view.</summary>
internal static class LiveTableKey
{
    /// <summary>
    /// Returns <c>table:{schema}.{name}</c>, or <c>table:{name}</c> for a table the model maps without a schema.
    /// The names are the model's, so every process that shares the model builds the same key.
    /// </summary>
    public static string Create(string name, string? schema)
    {
        return schema is null ? $"table:{name}" : $"table:{schema}.{name}";
    }
}
