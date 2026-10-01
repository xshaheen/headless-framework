// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features.PostgreSql;

/// <summary>Connection and command options for the PostgreSQL features storage provider.</summary>
[PublicAPI]
public sealed class PostgreSqlFeaturesOptions : RelationalFeaturesOptions;

internal sealed class PostgreSqlFeaturesOptionsValidator
    : RelationalFeaturesOptionsValidator<PostgreSqlFeaturesOptions>;
