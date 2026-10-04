// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Hosting;

/// <summary>A service declared through <c>RequireSingletonService</c> whose effective registration is not a singleton.</summary>
/// <param name="Requirement">The declared requirement.</param>
/// <param name="Lifetime">The lifetime of the registration the container would resolve.</param>
[PublicAPI]
public sealed record ServiceLifetimeViolation(RequiredServiceRegistration Requirement, ServiceLifetime Lifetime);
