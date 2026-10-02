// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings.PostgreSql;

/// <summary>Options for the PostgreSQL settings storage provider.</summary>
[PublicAPI]
public sealed class PostgreSqlSettingsOptions : RelationalSettingsOptions;

internal sealed class PostgreSqlSettingsOptionsValidator
    : RelationalSettingsOptionsValidator<PostgreSqlSettingsOptions>;
