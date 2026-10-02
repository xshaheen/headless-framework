// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions.SqlServer;

/// <summary>Connection and command options for the SQL Server permissions storage provider.</summary>
[PublicAPI]
public sealed class SqlServerPermissionsOptions : RelationalPermissionsOptions;

internal sealed class SqlServerPermissionsOptionsValidator
    : RelationalPermissionsOptionsValidator<SqlServerPermissionsOptions>;
