// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions.PostgreSql;

/// <summary>Connection and command options for the PostgreSQL permissions storage provider.</summary>
[PublicAPI]
public sealed class PostgreSqlPermissionsOptions : RelationalPermissionsOptions;

internal sealed class PostgreSqlPermissionsOptionsValidator
    : RelationalPermissionsOptionsValidator<PostgreSqlPermissionsOptions>;
