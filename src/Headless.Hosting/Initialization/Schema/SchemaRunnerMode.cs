// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.Hosting.Initialization.Schema;

/// <summary>What the schema runner does at host startup.</summary>
[PublicAPI]
public enum SchemaRunnerMode
{
    /// <summary>Apply every missing step, then start. The zero-config default for development and for hosts allowed to run DDL.</summary>
    Apply = 0,

    /// <summary>
    /// Write nothing. Fail startup when a registered step is missing or its checksum changed, for hosts whose DDL is
    /// deployed from the exported script. A database that lacks a step then fails at boot, not at the first query.
    /// </summary>
    Verify = 1,
}
