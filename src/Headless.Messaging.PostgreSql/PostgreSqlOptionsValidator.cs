// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;

namespace Headless.Messaging.PostgreSql;

internal sealed class PostgreSqlOptionsValidator : IValidateOptions<PostgreSqlOptions>
{
    public ValidateOptionsResult Validate(string? name, PostgreSqlOptions options)
    {
        if (options.DataSource is null && string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return ValidateOptionsResult.Fail(
                "PostgreSQL messaging storage requires either a DataSource or ConnectionString. "
                    + "Configure via UsePostgreSql(connectionString) or UsePostgreSql(options => options.ConnectionString = ...)"
            );
        }

        return ValidateOptionsResult.Success;
    }
}
