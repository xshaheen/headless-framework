// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Checks;
using Headless.Hosting;
using Headless.Search;
using Headless.Search.PostgreSql;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Maps Arabic search into an EF Core model on PostgreSQL: the query function, and stored search keys with their
/// trigram indexes.
/// </summary>
[PublicAPI]
public static class HeadlessArabicSearchModelBuilderExtensions
{
    private const string _VersionAnnotation = "Headless:ArabicSearch:Version";
    private const string _SchemaAnnotation = "Headless:ArabicSearch:Schema";

    /// <summary>
    /// Maps <see cref="ArabicSearchFunctions.Key" /> to <paramref name="version" />'s database function in
    /// <paramref name="schema" />, declares the <c>pg_trgm</c> extension the key indexes need, and fixes the version
    /// and schema every <c>HasArabicSearchKey</c> in this model uses, so a key and its query cannot disagree.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="version">The rule version the model's keys are built with.</param>
    /// <param name="schema">
    /// The schema holding the function: the <see cref="PostgreSqlSearchOptions.Schema" /> the search provider was
    /// registered with. The default is <see cref="HeadlessStorageDefaults.Schema" />.
    /// </param>
    /// <returns>The model builder, for chaining.</returns>
    /// <remarks>Call it before any entity configuration that calls <c>HasArabicSearchKey</c>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="modelBuilder" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="schema" /> is <see langword="null" /> or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version" /> is not a defined version.</exception>
    public static ModelBuilder HasHeadlessArabicSearch(
        this ModelBuilder modelBuilder,
        ArabicSearchVersion version,
        string schema = HeadlessStorageDefaults.Schema
    )
    {
        Argument.IsNotNull(modelBuilder);
        Argument.IsNotNullOrWhiteSpace(schema);

        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder
            .HasDbFunction(() => ArabicSearchFunctions.Key(default))
            .HasSchema(schema)
            .HasName(PostgreSqlArabicSearchFunction.Name(version))
            .IsNullable();
        modelBuilder.Model.SetAnnotation(_VersionAnnotation, (int)version);
        modelBuilder.Model.SetAnnotation(_SchemaAnnotation, schema);

        return modelBuilder;
    }

    /// <summary>
    /// Declares <paramref name="key" /> as the stored search key of <paramref name="source" />: a
    /// <c>GENERATED ALWAYS AS (…) STORED</c> column computed by the model's search function, with a GIN
    /// <c>gin_trgm_ops</c> index.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="source">The text property the key is built from; it keeps the original text for display.</param>
    /// <param name="key">The key property. The database writes it; the application only reads it.</param>
    /// <param name="sourceColumn">
    /// The source's column name, when it is configured after this call or renamed by a convention that runs later.
    /// The default is the source's column name at the time of this call.
    /// </param>
    /// <returns>The entity type builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">The model did not call <c>HasHeadlessArabicSearch</c> first.</exception>
    public static EntityTypeBuilder<TEntity> HasArabicSearchKey<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, string?>> source,
        Expression<Func<TEntity, string?>> key,
        string? sourceColumn = null
    )
        where TEntity : class
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(source);
        Argument.IsNotNull(key);

        return _HasArabicSearchKey(builder, source, builder.Property(key), sourceColumn);
    }

    /// <summary>
    /// Declares a shadow <see cref="string" /> property named <paramref name="keyProperty" /> as the stored search key of
    /// <paramref name="source" />, as the other overload does for a mapped property.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="source">The text property the key is built from.</param>
    /// <param name="keyProperty">The shadow key property; query it with <c>EF.Property&lt;string&gt;(entity, name)</c>.</param>
    /// <param name="sourceColumn">The source's column name, when it differs from its name at the time of this call.</param>
    /// <returns>The entity type builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="keyProperty" /> is empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">The model did not call <c>HasHeadlessArabicSearch</c> first.</exception>
    public static EntityTypeBuilder<TEntity> HasArabicSearchKey<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, string?>> source,
        string keyProperty,
        string? sourceColumn = null
    )
        where TEntity : class
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(source);
        Argument.IsNotNullOrWhiteSpace(keyProperty);

        return _HasArabicSearchKey(builder, source, builder.Property<string?>(keyProperty), sourceColumn);
    }

    private static EntityTypeBuilder<TEntity> _HasArabicSearchKey<TEntity>(
        EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, string?>> source,
        PropertyBuilder<string?> key,
        string? sourceColumn
    )
        where TEntity : class
    {
        var model = builder.Metadata.Model;

        if (
            model.FindAnnotation(_VersionAnnotation)?.Value is not int version
            || model.FindAnnotation(_SchemaAnnotation)?.Value is not string schema
        )
        {
            throw new InvalidOperationException(
                $"{builder.Metadata.DisplayName()} declares an Arabic search key, but the model has no search version. "
                    + "Call modelBuilder.HasHeadlessArabicSearch(version) before configuring the entity."
            );
        }

        var column = sourceColumn ?? builder.Property(source).Metadata.GetColumnName();
        key.HasComputedColumnSql(
            PostgreSqlArabicSearchFunction.Call(schema, (ArabicSearchVersion)version, column),
            stored: true
        );
        builder.HasIndex(key.Metadata.Name).HasMethod("gin").HasOperators("gin_trgm_ops");

        return builder;
    }
}
