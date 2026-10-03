// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.AuditLog;

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
