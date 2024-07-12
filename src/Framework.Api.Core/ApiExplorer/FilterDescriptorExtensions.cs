using Framework.Arguments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Framework.Api.Core.ApiExplorer;

/// <summary><see cref="IList{FilterDescriptor}"/> extension methods.</summary>
public static class FilterDescriptorExtensions
{
    /// <summary>Gets the authorization policy requirements.</summary>
    /// <param name="filterDescriptors">The filter descriptors.</param>
    /// <returns>A collection of authorization policy requirements.</returns>
    public static IReadOnlyList<IAuthorizationRequirement> GetPolicyRequirements(
        this IList<FilterDescriptor> filterDescriptors
    )
    {
        Argument.IsNotNull(filterDescriptors);

        var policyRequirements = new List<IAuthorizationRequirement>();

        for (var i = filterDescriptors.Count - 1; i >= 0; --i)
        {
            var filterDescriptor = filterDescriptors[i];

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
