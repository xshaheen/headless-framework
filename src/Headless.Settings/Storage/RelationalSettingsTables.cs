// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;

namespace Headless.Settings;

/// <summary>
/// The settings tables' names in one dialect's convention and their configured column lengths, shared by the
/// relational repositories and each provider's schema contribution so the DDL and the statements name the same
/// objects and size the same columns.
/// </summary>
internal sealed class RelationalSettingsTables
{
    public RelationalSettingsTables(ISqlDialect dialect, SettingsStorageOptions options)
    {
        Dialect = dialect;
        Options = options;
        Schema = options.Schema;
        // A configured name is used verbatim; the default follows the dialect's convention.
        ValuesName = options.SettingValuesTableName ?? dialect.Name(DefaultValuesPascalName);
        DefinitionsName = options.SettingDefinitionsTableName ?? dialect.Name(DefaultDefinitionsPascalName);
        Values = dialect.Qualify(Schema, ValuesName);
        Definitions = dialect.Qualify(Schema, DefinitionsName);
    }

    public const string DefaultValuesPascalName = "SettingValues";
    public const string DefaultDefinitionsPascalName = "SettingDefinitions";

    public ISqlDialect Dialect { get; }

    /// <summary>The storage options the names came from; the source of every column length.</summary>
    public SettingsStorageOptions Options { get; }

    public string Schema { get; }

    /// <summary>The unqualified, unquoted value table name.</summary>
    public string ValuesName { get; }

    /// <summary>The unqualified, unquoted definition table name.</summary>
    public string DefinitionsName { get; }

    /// <summary>The quoted, schema-qualified value table.</summary>
    public string Values { get; }

    /// <summary>The quoted, schema-qualified definition table.</summary>
    public string Definitions { get; }

    /// <summary>Returns a column, in the dialect's convention and quoted.</summary>
    public string Column(string pascalName) => Dialect.Quote(Dialect.Name(pascalName));
}
