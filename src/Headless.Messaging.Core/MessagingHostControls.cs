// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Messaging.CircuitBreaker;
using Headless.Reliability;
using Microsoft.Extensions.Configuration;

namespace Headless.Messaging;

/// <summary>The host-level controls applied to the consumers when the consumer registry is built.</summary>
/// <param name="Tunings">Every <c>Tune</c> call, in registration order.</param>
/// <param name="ConsumeOnly">Every <c>ConsumeOnly</c> entry.</param>
/// <param name="Configuration">The host configuration that tunes consumers by identity, if any.</param>
/// <param name="DefaultFailurePolicy">The failure policy of a competing consumer that neither declares nor tunes one.</param>
internal sealed record MessagingHostControls(
    IReadOnlyList<ConsumerTuning> Tunings,
    IReadOnlyList<string> ConsumeOnly,
    IConfiguration? Configuration,
    FailurePolicyDefinition DefaultFailurePolicy
);
