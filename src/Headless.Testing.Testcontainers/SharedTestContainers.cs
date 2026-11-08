// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Docker.DotNet;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Opt-in sharing of one long-lived server container per image across test processes. Each fixture still gets its own
/// database or virtual host on that server, so only the server process is shared.
/// </summary>
/// <remarks>
/// <para>
/// Sharing is off unless <see cref="Variable"/> is <c>true</c>. Off, every fixture starts a fresh container that the
/// resource reaper removes after the run, exactly as before. On, the fixture attaches to (or creates) the container
/// named <c>headless-testing-{kind}-{hash}</c>; the reaper never removes it, so a host that turns sharing on also owns
/// removing idle shared containers, which carry the <see cref="Label"/> label.
/// </para>
/// <para>
/// The container name is the cross-process lock: two processes that both find no shared container race to create it,
/// Docker refuses the second name with 409 Conflict, and the loser attaches to the winner's container.
/// </para>
/// </remarks>
[PublicAPI]
public static class SharedTestContainers
{
    /// <summary>The environment variable that turns container sharing on when set to <c>true</c>.</summary>
    public const string Variable = "HEADLESS_TESTCONTAINERS_SHARED";

    /// <summary>The label every shared container carries, so a host can find and remove idle ones.</summary>
    public const string Label = "org.headless.testing.shared";

    private const int _MaxAttachAttempts = 5;

    // Part of every shared container's name. Bump it whenever owner naming, locking, or the sweep changes, so a
    // checkout on the new protocol never shares a server with one still running the old, whose locks the new sweep
    // would not recognise.
    private const string _ProtocolVersion = "v1";

    /// <summary>Gets whether this process shares server containers across fixtures and processes.</summary>
    public static bool IsEnabled { get; } =
        string.Equals(Environment.GetEnvironmentVariable(Variable), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the deterministic container name for a shared server of <paramref name="kind"/> built from
    /// <paramref name="image"/> and <paramref name="settings"/>. A change to either gives a new name, so a run never
    /// attaches to a server configured differently.
    /// </summary>
    internal static string ContainerName(string kind, string image, string settings)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(_ProtocolVersion + "\n" + image + "\n" + settings));
        return $"headless-testing-{kind}-{Convert.ToHexStringLower(hash, 0, 6)}";
    }

    /// <summary>
    /// Marks <paramref name="builder"/> as the shared container <paramref name="name"/>: a fixed name, the sharing
    /// label, and reuse keyed by that name.
    /// </summary>
    internal static TBuilder AsShared<TBuilder, TContainer>(TBuilder builder, string name)
        where TBuilder : IContainerBuilder<TBuilder, TContainer, IContainerConfiguration>
        where TContainer : IContainer
    {
        // Reuse is banned for test code because the reaper never removes a reused container and a reused database
        // carries one run's rows into the next. This helper is the one exception: it runs only when the host opted in
        // (and so removes idle shared containers itself), and every fixture on the container works in a database or
        // virtual host of its own that it drops on dispose and that a later run sweeps if this process dies first.
#pragma warning disable RS0030
        return builder.WithName(name).WithLabel(Label, "true").WithReuse(_ => name);
#pragma warning restore RS0030
    }

    /// <summary>
    /// Starts the container <paramref name="build"/> returns, retrying with a fresh build when another process created
    /// the same shared container first.
    /// </summary>
    internal static async Task<TContainer> StartAsync<TContainer>(
        Func<TContainer> build,
        CancellationToken cancellationToken
    )
        where TContainer : IContainer
    {
        for (var attempt = 1; ; attempt++)
        {
            var container = build();

            try
            {
                await container.StartAsync(cancellationToken).ConfigureAwait(false);
                return container;
            }
            catch (DockerApiException e) when (e.StatusCode == HttpStatusCode.Conflict && attempt < _MaxAttachAttempts)
            {
                // Another process won the create race; its container now carries the name and reuse label, so the
                // next build finds and attaches to it.
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
