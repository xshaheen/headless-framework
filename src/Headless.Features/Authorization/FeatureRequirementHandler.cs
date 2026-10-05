// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Authorization;

namespace Headless.Features;

/// <summary>
/// Satisfies each <see cref="FeatureRequirement"/> whose features are enabled for the ambient tenant, and every one
/// of them when the policy carries the <see cref="DisableFeatureCheckAttribute"/> marker. A disabled feature leaves
/// its requirement pending without calling <c>Fail</c>, as the permission handlers do, so ASP.NET Core still reports
/// the requirement in <see cref="AuthorizationFailure.FailedRequirements"/> and the HTTP host can tell a feature
/// failure from any other.
/// </summary>
internal sealed class FeatureRequirementHandler(IFeatureManager featureManager) : IAuthorizationHandler
{
    private readonly IFeatureManager _featureManager = Argument.IsNotNull(featureManager);

    public async Task HandleAsync(AuthorizationHandlerContext context)
    {
        List<FeatureRequirement>? requirements = null;
        var checkDisabled = false;

        // Requirements, not PendingRequirements: Succeed mutates the pending set, and the opt-out marker may follow
        // the requirements it switches off in the combined policy.
        foreach (var requirement in context.Requirements)
        {
            switch (requirement)
            {
                case FeatureCheckDisabledRequirement:
                    checkDisabled = true;
                    context.Succeed(requirement);
                    break;
                case FeatureRequirement featureRequirement:
                    (requirements ??= []).Add(featureRequirement);
                    break;
            }
        }

        if (requirements is null)
        {
            return;
        }

        foreach (var requirement in requirements)
        {
            // The handler contract carries no token; a feature read is a cache hit after the first request.
            if (
                checkDisabled
                || await _featureManager
                    .IsEnabledAsync(requirement.RequiresAll, requirement.FeatureNames)
                    .ConfigureAwait(false)
            )
            {
                context.Succeed(requirement);
            }
        }
    }
}
