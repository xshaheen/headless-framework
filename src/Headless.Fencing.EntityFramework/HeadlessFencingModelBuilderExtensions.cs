// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Checks;
using Headless.Fencing;
using Headless.Sql;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.EntityFrameworkCore;

/// <summary>Maps a lease kept as columns on an entity's own row, and the database clock its candidate queries use.</summary>
[PublicAPI]
public static class HeadlessFencingModelBuilderExtensions
{
    // Marks a complex property HasRowLease mapped, so the descriptor refuses a lease mapped by hand with other types.
    internal const string RowLeaseAnnotation = "Headless:Fencing:RowLease";

    /// <summary>
    /// Maps <paramref name="lease" /> as a complex property onto the five lease columns of the entity's table: the
    /// generation (<see cref="long" />, default 0), the state (<see cref="RowLeaseState" /> stored as a 16-bit integer,
    /// default 0), the expiry (nullable), the holder (nullable, at most <see cref="FencingFieldLimits.HolderMaxLength" />
    /// characters), and the takeover count (<see cref="int" />, default 0).
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="lease">The entity's lease property.</param>
    /// <param name="columns">
    /// The column names, in the table's naming convention, or <see langword="null" /> for the defaults
    /// (<c>lease_generation</c>, <c>lease_state</c>, <c>lease_expires_at</c>, <c>lease_holder</c>,
    /// <c>lease_takeover_count</c>).
    /// </param>
    /// <returns>The entity type builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The lease columns change only through <see cref="FencedRows" />: <c>SaveChanges</c> writes them when it inserts
    /// the row, so a new row starts <see cref="RowLeaseState.Idle" /> or as the entity holds it, and never on an
    /// update, so an entity loaded before a transition cannot overwrite the lease the transition wrote.
    /// </para>
    /// <para>
    /// Build the descriptor the transitions take with <c>context.FencedRowTable(...)</c>, which reads these column names
    /// back from the model.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder" /> or <paramref name="lease" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">A column name is empty or white space.</exception>
    public static EntityTypeBuilder<TEntity> HasRowLease<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, RowLeaseSnapshot>> lease,
        FencedRowColumns? columns = null
    )
        where TEntity : class
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(lease);

        columns ??= new FencedRowColumns();
        Argument.IsNotNullOrWhiteSpace(columns.Generation, paramName: nameof(columns));
        Argument.IsNotNullOrWhiteSpace(columns.State, paramName: nameof(columns));
        Argument.IsNotNullOrWhiteSpace(columns.ExpiresAt, paramName: nameof(columns));
        Argument.IsNotNullOrWhiteSpace(columns.Holder, paramName: nameof(columns));
        Argument.IsNotNullOrWhiteSpace(columns.TakeoverCount, paramName: nameof(columns));

        builder.ComplexProperty(
            lease,
            complex =>
            {
                complex.HasPropertyAnnotation(RowLeaseAnnotation, value: true);
                _LeaseColumn(complex.Property(l => l.Generation).HasColumnName(columns.Generation).HasDefaultValue(0L));
                _LeaseColumn(
                    complex
                        .Property(l => l.State)
                        .HasColumnName(columns.State)
                        .HasConversion<short>()
                        .HasDefaultValue(RowLeaseState.Idle)
                );
                _LeaseColumn(complex.Property(l => l.ExpiresAt).HasColumnName(columns.ExpiresAt));
                _LeaseColumn(
                    complex
                        .Property(l => l.Holder)
                        .HasColumnName(columns.Holder)
                        .HasMaxLength(FencingFieldLimits.HolderMaxLength)
                );
                _LeaseColumn(
                    complex.Property(l => l.TakeoverCount).HasColumnName(columns.TakeoverCount).HasDefaultValue(0)
                );
            }
        );

        return builder;
    }

    /// <summary>
    /// Maps the database clock that <c>WhereLeaseLive</c> and <c>WhereLeaseGrantable</c> compare a lease's expiry with:
    /// <c>clock_timestamp()</c> on PostgreSQL, <c>TODATETIMEOFFSET(SYSUTCDATETIME(), 0)</c> on SQL Server, and on
    /// SQLite <c>'now'</c> in the text shape the SQLite dialect stores instants in.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="dialect">The dialect of the database the model runs on, the one its descriptors are built with.</param>
    /// <returns>The model builder, for chaining.</returns>
    /// <remarks>
    /// The clock serves candidate selection only. A query that reads it decides nothing: it reads the clock before any
    /// row lock, so the transition run on a candidate re-checks the lease after its lock.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="modelBuilder" /> or <paramref name="dialect" /> is <see langword="null" />.</exception>
    /// <exception cref="NotSupportedException"><paramref name="dialect" /> is not the PostgreSQL, SQL Server, or SQLite dialect.</exception>
    public static ModelBuilder UseFencingDatabaseClock(this ModelBuilder modelBuilder, ISqlDialect dialect)
    {
        Argument.IsNotNull(modelBuilder);
        Argument.IsNotNull(dialect);

        var clock = FencingDatabaseClock.Clock(dialect);

        modelBuilder
            .HasDbFunction(FencingDatabaseClock.IsBeforeExpiryMethod)
            .HasTranslation(arguments => FencingDatabaseClock.Translate(arguments[0], clock));

        return modelBuilder;
    }

    private static void _LeaseColumn<TProperty>(ComplexTypePropertyBuilder<TProperty> property)
    {
        property.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }
}
