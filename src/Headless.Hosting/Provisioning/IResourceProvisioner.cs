// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>
/// Provisions external state a feature needs before it can run, such as database tables, broker queues, or a storage
/// container, so a deployment creates it with <c>resources provision</c> instead of the running host creating it.
/// </summary>
/// <remarks>
/// <para>
/// Register one with <c>AddResourceProvisioner</c>, or a set derived from registrations with
/// <c>AddResourceProvisionerSource</c>. The <c>resources</c> verb of <c>RunHeadlessAsync</c>, the startup step of
/// <c>AddDeployStepsOnStartup</c>, and the test helpers on <see cref="IServiceProvider" /> all run every provisioner
/// through the same operations.
/// </para>
/// <para>
/// A provisioner states which destructive operations it supports with <see cref="CanPurge" /> and
/// <see cref="CanDelete" />; callers skip an unsupported operation and report it as such, without calling it. Any
/// exception an operation throws is a failure. Every operation must be idempotent: a deployment re-runs
/// <c>provision</c> on every release.
/// </para>
/// </remarks>
[PublicAPI]
public interface IResourceProvisioner
{
    /// <summary>
    /// Gets the kind of resource, such as <c>Schema</c> or <c>RabbitMQ</c>. The <c>--kind</c> filter of the
    /// <c>resources</c> verb matches it.
    /// </summary>
    string Kind { get; }

    /// <summary>
    /// Gets the resource's name within its kind. The <c>--name</c> filter of the <c>resources</c> verb matches it. It
    /// is printed to deploy logs, so it never carries a host name, connection string, or credential.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets whether <see cref="PurgeAsync" /> applies to this resource. Default: <see langword="true" />.
    /// </summary>
    bool CanPurge => true;

    /// <summary>
    /// Gets whether <see cref="DeleteAsync" /> applies to this resource. Default: <see langword="true" />.
    /// </summary>
    bool CanDelete => true;

    /// <summary>Creates whatever the resource lacks, and changes nothing that already exists as expected.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the resource is provisioned.</returns>
    Task ProvisionAsync(CancellationToken cancellationToken);

    /// <summary>Checks, without writing, that the resource is provisioned and reachable.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the resource is ready.</returns>
    /// <exception cref="ResourceProvisioningException">The resource is reachable but not provisioned as expected.</exception>
    Task VerifyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Removes the resource's data, such as queued messages or stored rows, and keeps its structure. Called only when
    /// <see cref="CanPurge" /> is <see langword="true" />.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the data is removed.</returns>
    Task PurgeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Deletes what <see cref="ProvisionAsync" /> created. Called only when <see cref="CanDelete" /> is
    /// <see langword="true" />.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the resource is deleted.</returns>
    Task DeleteAsync(CancellationToken cancellationToken);

    /// <summary>Reads the resource's current state as ordered name and value pairs, for an operator to read.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The state, in display order.</returns>
    Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Selects, from every registered provisioner, the ones this resource needs provisioned first, for example the
    /// database that stores a queue. Default: none.
    /// </summary>
    /// <param name="provisioners">Every registered provisioner, this one included.</param>
    /// <returns>The provisioners to run before this one. Delete runs them after it.</returns>
    IEnumerable<IResourceProvisioner> DependsOn(IReadOnlyList<IResourceProvisioner> provisioners) => [];
}

/// <summary>
/// Provides provisioners whose set depends on other registrations, such as one queue per registered consumer. It is
/// asked only when an operation runs, after the container is built.
/// </summary>
[PublicAPI]
public interface IResourceProvisionerSource
{
    /// <summary>Returns the provisioners this source provides.</summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The provisioners; empty when there is nothing to provision.</returns>
    Task<IReadOnlyList<IResourceProvisioner>> GetProvisionersAsync(CancellationToken cancellationToken);
}
