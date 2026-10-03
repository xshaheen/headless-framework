// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Registration;
using Headless.Reliability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging;

/// <summary>One <c>Tune</c> call recorded in the service collection.</summary>
internal sealed record MessagingTuningContribution(ConsumerTuning Tuning);
