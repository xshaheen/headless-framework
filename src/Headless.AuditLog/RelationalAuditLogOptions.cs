// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.AuditLog;

/// <summary>Connection and command options every relational audit-log storage provider shares.</summary>
[PublicAPI]
public abstract class RelationalAuditLogOptions
{
    /// <summary>
    /// Gets or sets the connection string used to open connections for audit row writes and reads when the provider
    /// does not enlist in the caller's transaction. Required; validated non-empty on startup.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Gets or sets the timeout applied to every command this provider runs. Default: 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal int CommandTimeoutSeconds => (int)CommandTimeout.TotalSeconds;
}

internal abstract class RelationalAuditLogOptionsValidator<TOptions> : AbstractValidator<TOptions>
    where TOptions : RelationalAuditLogOptions
{
    protected RelationalAuditLogOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromSeconds(int.MaxValue));
    }
}

/// <summary>Validates <see cref="AuditLogStorageOptions"/> for one relational provider's identifiers and column types.</summary>
internal sealed class RelationalAuditLogStorageOptionsValidator : AbstractValidator<AuditLogStorageOptions>
{
    public RelationalAuditLogStorageOptionsValidator(
        StorageProvider provider,
        IReadOnlyCollection<AuditLogJsonColumnType> jsonColumnTypes,
        string jsonColumnTypeMessage
    )
    {
        // Every provider measures derived names against PostgreSQL's limit, so a table name valid on one database
        // stays valid on the others.
        RuleFor(x => x.Schema).IsValidIdentifierFor(provider);
        RuleFor(x => x.TableName)
            .IsValidIdentifierFor(provider)
            .FitsDerivedPostgreSqlNames(AuditLogStorageNames.Indexes)
            .When(x => x.TableName is not null);
        When(
            x => x.JsonColumnType.HasValue,
            () =>
            {
                RuleFor(x => x.JsonColumnType!.Value).Must(jsonColumnTypes.Contains).WithMessage(jsonColumnTypeMessage);
            }
        );
        // The column type is spliced into DDL, so only a plain type name with an optional precision is accepted.
        RuleFor(x => x.CreatedAtColumnType!)
            .MaximumLength(64)
            .Matches(@"^[A-Za-z][A-Za-z0-9 ]*(\([0-9]+\))?$")
            .When(x => !string.IsNullOrEmpty(x.CreatedAtColumnType));
        RuleFor(x => x.PurgeAuthority).IsInEnum();
        RuleFor(x => x.Partitioning).IsInEnum();
        RuleFor(x => x.PartitionMonthsAhead).InclusiveBetween(0, PostgreSqlAuditLogSql.MaxMonthsAhead);
        RuleFor(x => x.MinimumRetention!.Value)
            .GreaterThanOrEqualTo(TimeSpan.FromSeconds(1))
            .When(x => x.MinimumRetention.HasValue);

        if (provider is StorageProvider.PostgreSql)
        {
            // The owner's purge function is installed beside the trigger it is the only way through.
            RuleFor(x => x.PurgeAuthority)
                .Must((options, authority) => authority is not AuditLogPurgeAuthority.Owner || options.AppendOnly)
                .WithMessage(
                    $"{nameof(AuditLogStorageOptions.PurgeAuthority)} {nameof(AuditLogPurgeAuthority.Owner)} needs {nameof(AuditLogStorageOptions.AppendOnly)}: without the trigger, any login with DELETE on the table purges it."
                );
            RuleFor(x => x.MinimumRetention)
                .Must(
                    (options, retention) =>
                        retention is null
                        || options.PurgeAuthority is AuditLogPurgeAuthority.Owner
                        || options.Partitioning is AuditLogPartitioning.Monthly
                )
                .WithMessage(
                    $"{nameof(AuditLogStorageOptions.MinimumRetention)} is enforced by the owner's purge function and the partition drop function, so it needs {nameof(AuditLogStorageOptions.PurgeAuthority)} {nameof(AuditLogPurgeAuthority.Owner)} or {nameof(AuditLogStorageOptions.Partitioning)} {nameof(AuditLogPartitioning.Monthly)}."
                );
        }
        else
        {
            RuleFor(x => x.PurgeAuthority)
                .Equal(AuditLogPurgeAuthority.SessionSetting)
                .WithMessage(
                    $"{nameof(AuditLogStorageOptions.PurgeAuthority)} {nameof(AuditLogPurgeAuthority.Owner)} is supported by the PostgreSql audit-log provider only."
                );
            RuleFor(x => x.Partitioning)
                .Equal(AuditLogPartitioning.None)
                .WithMessage(
                    $"{nameof(AuditLogStorageOptions.Partitioning)} is supported by the PostgreSql audit-log provider only."
                );
            RuleFor(x => x.MinimumRetention)
                .Null()
                .WithMessage(
                    $"{nameof(AuditLogStorageOptions.MinimumRetention)} is supported by the PostgreSql audit-log provider only."
                );
        }
    }
}
