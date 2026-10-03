// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Couchbase;
using Couchbase.Core.Exceptions;
using Couchbase.KeyValue;
using Couchbase.Management.Collections;
using Couchbase.Management.Query;
using Headless.Checks;
using Headless.Couchbase.Clusters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Headless.Couchbase.Managers;

/// <summary>Outcome of a <c>CreateScopeAsync</c> call.</summary>
/// <remarks>Additional members may be added in future versions; handle unrecognized values defensively.</remarks>
[PublicAPI]
public enum CreateScopeStatus
{
    /// <summary>Default, unassigned outcome. Not returned by <c>CreateScopeAsync</c>; present as the zero sentinel.</summary>
    Unknown = 0,

    /// <summary>The scope already existed; no action was taken.</summary>
    Exist = 1,

    /// <summary>The scope was successfully created.</summary>
    Success = 2,

    /// <summary>The operation failed after all retries were exhausted.</summary>
    Failed = 3,
}
