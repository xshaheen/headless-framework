// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Hosting.DependencyInjection;

/// <summary>
/// Thrown at host startup when one or more services declared through
/// <c>IServiceCollection.RequireSingletonService&lt;T&gt;(…)</c> are registered as scoped or transient. Every
/// violation collected across the whole host is reported in a single throw.
/// </summary>
[PublicAPI]
public sealed class InvalidServiceLifetimeException(string message, IReadOnlyList<ServiceLifetimeViolation> violations)
    : InvalidOperationException(message)
{
    /// <summary>The requirements whose registration is not a singleton, in declaration order.</summary>
    public IReadOnlyList<ServiceLifetimeViolation> Violations { get; } = Argument.IsNotNull(violations);
}
