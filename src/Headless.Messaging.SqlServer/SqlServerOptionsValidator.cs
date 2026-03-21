// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Messaging.SqlServer;

internal sealed class SqlServerOptionsValidator : AbstractValidator<SqlServerOptions>
{
    public SqlServerOptionsValidator()
    {
        RuleFor(x => x)
            .Must(x => x.DbContextType is not null || !string.IsNullOrWhiteSpace(x.ConnectionString))
            .WithMessage(
                "SQL Server messaging storage requires either a DbContextType or ConnectionString. "
                    + "Configure via UseSqlServer(connectionString) or UseSqlServer(options => options.ConnectionString = ...)"
            );
    }
}
