// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Features.Resources;
using Microsoft.AspNetCore.Authorization;

namespace Headless.Features;

/// <summary>
/// Satisfies each <see cref="FeatureRequirement"/> whose features are enabled for the ambient tenant, and every one
/// of them when the policy carries the <see cref="DisableFeatureCheckAttribute"/> marker. A disabled feature fails the
/// evaluation with the localized "feature currently unavailable" text as its <see cref="AuthorizationFailureReason"/>,
/// so the caller learns why the request was refused; the reason names no feature, because it reaches the caller.
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
            else
            {
                // Resolved here, during the request, so the text follows the request culture.
                context.Fail(
                    new AuthorizationFailureReason(this, MessageDescriber.FeatureCurrentlyUnavailable().Description)
                );
            }
        }
    }
}
