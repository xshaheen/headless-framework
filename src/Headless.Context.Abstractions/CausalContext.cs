// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Context;

/// <summary>
/// The business correlation and the immediate cause of the work running on the current async flow.
/// </summary>
/// <param name="CorrelationId">The identifier of the root business operation.</param>
/// <param name="CausationId">
/// The identifier of the object whose work is running: a job run id, or a received message's storage id.
/// </param>
[PublicAPI]
public sealed record CausalContext(string CorrelationId, string CausationId);
