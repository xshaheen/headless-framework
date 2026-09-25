// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting.Validation;

/// <summary>
/// A cheap correctness check that must pass before the host starts, such as an EF model that maps a feature's
/// entities or a required service that has a registration.
/// </summary>
/// <remarks>
/// <para>
/// Register one with <c>IServiceCollection.AddStartupValidator</c>. The host runs every registered validator once, in
/// <c>IHostedLifecycleService.StartingAsync</c>, before any hosted service's <c>StartAsync</c>, and reports all
/// failures together, so an operator fixes every misconfiguration in one pass instead of one restart per problem.
/// </para>
/// <para>
/// Throw to fail startup; the exception message is what the operator reads, so name the fix. Return normally to pass,
/// and log through an injected logger for advisory findings that should not block startup.
/// </para>
/// <para>
/// Keep validators cheap and free of network I/O: every instance runs them on every start, and they have no switch
/// to turn them off. A diagnostic that probes a remote dependency or measures cost belongs in its own hosted service
/// with a mode, not here.
/// </para>
/// <para>
/// The <c>Headless</c> prefix keeps the name distinct from <c>Microsoft.Extensions.Options.IStartupValidator</c>, which
/// most files that register services also import.
/// </para>
/// </remarks>
[PublicAPI]
public interface IHeadlessStartupValidator
{
    /// <summary>Checks the configuration this validator owns and throws when it is wrong.</summary>
    /// <param name="cancellationToken">Cancelled when host startup is aborted.</param>
    /// <returns>A task that completes when the check passes.</returns>
    Task ValidateAsync(CancellationToken cancellationToken);
}
