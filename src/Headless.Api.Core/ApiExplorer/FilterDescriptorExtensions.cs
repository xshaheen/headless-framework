// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Headless.Api.ApiExplorer;

/// <summary>Extension methods for <see cref="IList{FilterDescriptor}"/>.</summary>
public static class FilterDescriptorExtensions
{
    /// <summary>
    /// Collects all <see cref="IAuthorizationRequirement"/> instances from the effective authorization
    /// policy for a given action's filter pipeline. Stops at the first
    /// <see cref="AllowAnonymousFilter"/> found when scanning from the innermost filter outward,
    /// matching ASP.NET Core's short-circuit behavior.
    /// </summary>
    /// <param name="mvcFilterDescriptors">The filter descriptors for the action, ordered by scope.</param>
    /// <returns>
    /// Requirements from all <see cref="AuthorizeFilter"/>s that apply before any
    /// <see cref="AllowAnonymousFilter"/>. Returns an empty list when the action allows anonymous
    /// access or has no authorization filters.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="mvcFilterDescriptors"/> is <see langword="null"/>.
    /// </exception>
    public static IReadOnlyList<IAuthorizationRequirement> GetPolicyRequirements(
        this IList<FilterDescriptor> mvcFilterDescriptors
    )
    {
        Argument.IsNotNull(mvcFilterDescriptors);

        var policyRequirements = new List<IAuthorizationRequirement>();

        for (var i = mvcFilterDescriptors.Count - 1; i >= 0; --i)
        {
            var filterDescriptor = mvcFilterDescriptors[i];

            if (filterDescriptor.Filter is AllowAnonymousFilter)
            {
                break;
            }

            if (filterDescriptor.Filter is AuthorizeFilter { Policy: not null } authorizeFilter)
            {
                policyRequirements.AddRange(authorizeFilter.Policy.Requirements);
            }
        }

        return policyRequirements;
    }
}
