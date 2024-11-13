// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.Checks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Framework.Api.ApiExplorer;

/// <summary><see cref="IList{FilterDescriptor}"/> extension methods.</summary>
public static class FilterDescriptorExtensions
{
    /// <summary>Gets the authorization policy requirements.</summary>
    /// <param name="mvcFilterDescriptors">The filter descriptors.</param>
    /// <returns>A collection of authorization policy requirements.</returns>
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
