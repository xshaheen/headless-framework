// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Features.Values;

namespace Headless.Features.Filters;

/// <summary>Checks feature requirements declared on a method being invoked, throwing when a required feature is disabled.</summary>
public interface IMethodInvocationFeatureCheckerService
{
    /// <summary>
    /// Evaluates all <see cref="RequiresFeatureAttribute"/> instances on the method described by
    /// <paramref name="context"/> and throws if any required feature is not enabled.
    /// </summary>
    /// <param name="context">The context describing the method being checked.</param>
    Task CheckAsync(MethodInvocationFeatureCheckerContext context);
}

/// <summary>Carries the reflection metadata needed to check feature requirements for a single method invocation.</summary>
/// <param name="Method">The method being invoked.</param>
public sealed record MethodInvocationFeatureCheckerContext(MethodInfo Method);

/// <summary>Default <see cref="IMethodInvocationFeatureCheckerService"/> implementation.</summary>
public sealed class MethodInvocationFeatureCheckerService(IFeatureManager featureManager)
    : IMethodInvocationFeatureCheckerService
{
    /// <inheritdoc/>
    public async Task CheckAsync(MethodInvocationFeatureCheckerContext context)
    {
        if (_IsFeatureCheckDisabled(context))
        {
            return;
        }

        foreach (var requiresFeatureAttribute in _GetRequiredFeatureAttributes(context.Method))
        {
            await featureManager.EnsureEnabledAsync(requiresFeatureAttribute.IsAnd, requiresFeatureAttribute.Features);
        }
    }

    private static bool _IsFeatureCheckDisabled(MethodInvocationFeatureCheckerContext context)
    {
        return context.Method.GetCustomAttributes(inherit: true).OfType<DisableFeatureCheckAttribute>().Any();
    }

    private static IEnumerable<RequiresFeatureAttribute> _GetRequiredFeatureAttributes(MethodInfo methodInfo)
    {
        var attributes = methodInfo.GetCustomAttributes(inherit: true).OfType<RequiresFeatureAttribute>();

        if (methodInfo.IsPublic)
        {
            var requiresFeatureAttributes = methodInfo
                .DeclaringType!.GetCustomAttributes(inherit: true)
                .OfType<RequiresFeatureAttribute>();

            attributes = attributes.Union(requiresFeatureAttributes);
        }

        return attributes;
    }
}
