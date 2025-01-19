// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Framework.Features.Values;

namespace Framework.Features.Filters;

public interface IMethodInvocationFeatureCheckerService
{
    Task CheckAsync(MethodInvocationFeatureCheckerContext context);
}

public sealed record MethodInvocationFeatureCheckerContext(MethodInfo Method);

public sealed class MethodInvocationFeatureCheckerService(IFeatureManager featureManager)
    : IMethodInvocationFeatureCheckerService
{
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
