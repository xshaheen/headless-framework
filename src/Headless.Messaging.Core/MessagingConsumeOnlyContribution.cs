// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Registration;
using Headless.Reliability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging;

/// <summary>The <c>ConsumeOnly</c> entries one <c>AddHeadlessMessaging</c> call authored.</summary>
internal sealed record MessagingConsumeOnlyContribution(string[] Entries);
