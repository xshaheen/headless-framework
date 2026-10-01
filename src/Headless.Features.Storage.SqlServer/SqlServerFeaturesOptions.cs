// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features.SqlServer;

/// <summary>Connection and command options for the SQL Server features storage provider.</summary>
[PublicAPI]
public sealed class SqlServerFeaturesOptions : RelationalFeaturesOptions;

internal sealed class SqlServerFeaturesOptionsValidator : RelationalFeaturesOptionsValidator<SqlServerFeaturesOptions>;
