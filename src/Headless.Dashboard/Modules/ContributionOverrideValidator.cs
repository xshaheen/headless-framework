// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard;

/// <summary>
/// Builds the dashboard's manifest when the host starts, so a broken module manifest fails the start instead of the
/// first page load, and fails the start when a contribution override names an id no registered module declares: a
/// typo would otherwise leave the contribution the host meant to disable enabled.
/// </summary>
internal sealed class ContributionOverrideValidator(
    DashboardManifestProvider manifestProvider,
    IOptions<HeadlessDashboardOptions> options
) : IHeadlessStartupValidator
{
    /// <summary>Merges the module manifests once and fails the start when an override names no contribution.</summary>
    /// <exception cref="InvalidOperationException">
    /// A module's manifest is missing or invalid, or an override names an unknown contribution id.
    /// </exception>
    public Task ValidateAsync(CancellationToken cancellationToken)
    {
        var manifest = manifestProvider.Manifest;

        var unknown = options
            .Value.Contributions.AllIds()
            .Where(id => !manifest.KnownContributionIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (unknown.Length > 0)
        {
            throw new InvalidOperationException(
                "Headless dashboard contribution overrides name ids that no registered module declares: "
                    + string.Join(", ", unknown.Select(static id => $"'{id}'"))
                    + ". Check the ids passed to Contributions(...) or bound from Headless:Dashboard:Contributions."
            );
        }

        return Task.CompletedTask;
    }
}
