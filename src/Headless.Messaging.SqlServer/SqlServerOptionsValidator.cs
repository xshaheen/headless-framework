// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;

namespace Headless.Messaging.SqlServer;

internal sealed class SqlServerOptionsValidator : IValidateOptions<SqlServerOptions>
{
    public ValidateOptionsResult Validate(string? name, SqlServerOptions options)
    {
        if (options.DbContextType is null && string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return ValidateOptionsResult.Fail(
                "SQL Server messaging storage requires either a DbContextType or ConnectionString. "
                    + "Configure via UseSqlServer(connectionString) or UseSqlServer(options => options.ConnectionString = ...)"
            );
        }

        return ValidateOptionsResult.Success;
    }
}
