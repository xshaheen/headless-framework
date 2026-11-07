// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;

namespace Headless.Dashboard;

/// <summary>
/// The host's overrides of module contributions by id: nav entries, client and server commands, and related-object
/// groups. Applied on the server before the dashboard's manifest is built, so a disabled contribution never reaches
/// the browser.
/// </summary>
/// <remarks>
/// <para>
/// Set them in code through <see cref="HeadlessDashboardSetupBuilder.Contributions(Action{ContributionOverrides})"/>, or
/// bind them from configuration through <see cref="HeadlessDashboardSetupBuilder.Contributions(IConfiguration)"/>,
/// usually the <c>Headless:Dashboard:Contributions</c> section, whose keys are <c>Disable</c> (a list of ids) and
/// <c>Order</c> (a list of id lists). Both forms add up.
/// </para>
/// <para>
/// A disabled server command also answers <c>404</c> at its endpoint. An id that matches no contribution of any
/// registered module fails the host's start, because a typo would otherwise leave the contribution enabled.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class ContributionOverrides
{
    /// <summary>Gets or sets the ids of the disabled contributions. Bound from the <c>Disable</c> key.</summary>
    [ConfigurationKeyName("Disable")]
    public IList<string> DisabledIds { get; set; } = [];

    /// <summary>
    /// Gets or sets the ordering groups. Each group lists contribution ids in the order the shell shows them; the
    /// contributions a group does not name keep their places. Bound from the <c>Order</c> key.
    /// </summary>
    [ConfigurationKeyName("Order")]
    public IList<IList<string>> OrderGroups { get; set; } = [];

    /// <summary>Disables the contributions with the given ids.</summary>
    /// <param name="ids">The contribution ids, such as <c>messaging.nav.gateway</c>.</param>
    /// <returns>The same overrides for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ids"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="ids"/> is empty or holds an empty or null id.</exception>
    public ContributionOverrides Disable(params string[] ids)
    {
        Argument.IsNotNullOrEmpty(ids);
        Argument.HasNoNullOrWhiteSpaceElements(ids);

        foreach (var id in ids)
        {
            DisabledIds.Add(id);
        }

        return this;
    }

    /// <summary>
    /// Orders the contributions with the given ids relative to each other, in the order given. Contributions not
    /// named keep their places.
    /// </summary>
    /// <param name="ids">The contribution ids in display order, such as <c>jobs.run-related</c>.</param>
    /// <returns>The same overrides for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ids"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="ids"/> is empty or holds an empty or null id.</exception>
    public ContributionOverrides Order(params string[] ids)
    {
        Argument.IsNotNullOrEmpty(ids);
        Argument.HasNoNullOrWhiteSpaceElements(ids);

        OrderGroups.Add([.. ids]);

        return this;
    }

    internal IEnumerable<string> AllIds()
    {
        return DisabledIds.Concat(OrderGroups.SelectMany(static group => group));
    }
}
