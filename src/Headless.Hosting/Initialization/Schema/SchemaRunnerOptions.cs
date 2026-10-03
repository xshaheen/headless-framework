// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.Hosting.Initialization.Schema;

/// <summary>Startup options of the Headless schema runner, shared by every contributing feature.</summary>
[PublicAPI]
public sealed class SchemaRunnerOptions
{
    /// <summary>Gets or sets the startup mode. Default: <see cref="SchemaRunnerMode.Apply"/>.</summary>
    public SchemaRunnerMode Mode { get; set; } = SchemaRunnerMode.Apply;

    /// <summary>
    /// Gets or sets the timeout of every statement the runner sends, DDL included. Default:
    /// <see cref="SchemaRunner.DefaultCommandTimeout"/> (10 minutes), because an index build on a populated table
    /// outlasts an OLTP command timeout.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = SchemaRunner.DefaultCommandTimeout;

    /// <summary>Gets or sets how long a runner waits for another runner's lock. Default: <see cref="SchemaRunner.DefaultLockTimeout"/>.</summary>
    public TimeSpan LockTimeout { get; set; } = SchemaRunner.DefaultLockTimeout;
}
