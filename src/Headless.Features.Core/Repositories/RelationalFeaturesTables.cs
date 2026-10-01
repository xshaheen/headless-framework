// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;

namespace Headless.Features.Repositories;

/// <summary>
/// The features tables' names in one dialect's convention, shared by the relational repositories and each
/// provider's schema contribution so the DDL and the statements name the same objects.
/// </summary>
internal sealed class RelationalFeaturesTables
{
    public RelationalFeaturesTables(ISqlDialect dialect, FeaturesStorageOptions options)
    {
        Dialect = dialect;
        Schema = options.Schema;
        // A configured name is used verbatim; the default follows the dialect's convention.
        ValuesName = options.FeatureValuesTableName ?? dialect.Name("FeatureValues");
        DefinitionsName = options.FeatureDefinitionsTableName ?? dialect.Name("FeatureDefinitions");
        GroupsName = options.FeatureGroupDefinitionsTableName ?? dialect.Name("FeatureGroupDefinitions");
        Values = dialect.Qualify(Schema, ValuesName);
        Definitions = dialect.Qualify(Schema, DefinitionsName);
        Groups = dialect.Qualify(Schema, GroupsName);
    }

    public ISqlDialect Dialect { get; }

    public string Schema { get; }

    /// <summary>The unqualified, unquoted value table name.</summary>
    public string ValuesName { get; }

    /// <summary>The unqualified, unquoted feature definition table name.</summary>
    public string DefinitionsName { get; }

    /// <summary>The unqualified, unquoted group definition table name.</summary>
    public string GroupsName { get; }

    /// <summary>The quoted, schema-qualified value table.</summary>
    public string Values { get; }

    /// <summary>The quoted, schema-qualified feature definition table.</summary>
    public string Definitions { get; }

    /// <summary>The quoted, schema-qualified group definition table.</summary>
    public string Groups { get; }

    /// <summary>Returns a column, in the dialect's convention and quoted.</summary>
    public string Column(string pascalName) => Dialect.Quote(Dialect.Name(pascalName));
}
