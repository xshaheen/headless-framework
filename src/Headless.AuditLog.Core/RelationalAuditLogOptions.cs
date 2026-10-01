// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

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
    }
}
