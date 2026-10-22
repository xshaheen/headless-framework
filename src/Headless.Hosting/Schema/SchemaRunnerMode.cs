// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting.Schema;

/// <summary>What one <see cref="SchemaRunner.RunAsync"/> pass does.</summary>
[PublicAPI]
public enum SchemaRunnerMode
{
    /// <summary>Apply every missing step. What <c>resources provision</c> does for each feature.</summary>
    Apply = 0,

    /// <summary>
    /// Write nothing, and fail when a registered step is missing or its checksum changed. What <c>resources verify</c>
    /// does, and what a host start does for every contribution that is not export-only.
    /// </summary>
    Verify = 1,
}

/// <summary>Options of the Headless schema runner, shared by every contributing feature.</summary>
[PublicAPI]
public sealed class SchemaRunnerOptions
{
    /// <summary>
    /// Gets or sets the timeout of every statement the runner sends, DDL included. Default:
    /// <see cref="SchemaRunner.DefaultCommandTimeout"/> (10 minutes), because an index build on a populated table
    /// outlasts an OLTP command timeout.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = SchemaRunner.DefaultCommandTimeout;

    /// <summary>Gets or sets how long a runner waits for another runner's lock. Default: <see cref="SchemaRunner.DefaultLockTimeout"/>.</summary>
    public TimeSpan LockTimeout { get; set; } = SchemaRunner.DefaultLockTimeout;
}
