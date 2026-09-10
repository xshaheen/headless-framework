// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Hosting.DependencyInjection;

/// <summary>
/// Thrown at host startup when one or more services declared through
/// <c>IServiceCollection.RequireRegisteredService&lt;T&gt;(…)</c> are not registered. Every unsatisfied
/// requirement collected across the whole host is reported in a single throw so an operator fixes them
/// in one pass instead of restarting once per missing registration.
/// </summary>
[PublicAPI]
public sealed class MissingRequiredServiceException(
    string message,
    IReadOnlyList<RequiredServiceRegistration> missingServices
) : InvalidOperationException(message)
{
    /// <summary>The declared requirements that no registration satisfies, in declaration order.</summary>
    public IReadOnlyList<RequiredServiceRegistration> MissingServices { get; } = Argument.IsNotNull(missingServices);
}
