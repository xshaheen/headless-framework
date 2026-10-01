// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings.SqlServer;

/// <summary>Options for the SQL Server settings storage provider.</summary>
[PublicAPI]
public sealed class SqlServerSettingsOptions : RelationalSettingsOptions;

internal sealed class SqlServerSettingsOptionsValidator : RelationalSettingsOptionsValidator<SqlServerSettingsOptions>;
