// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Reflection;
using Framework.Features.Values;

namespace Framework.Features.Filters;

public interface IMethodInvocationFeatureCheckerService
{
    Task CheckAsync(MethodInvocationFeatureCheckerContext context);
}

public sealed record MethodInvocationFeatureCheckerContext(MethodInfo Method);

public sealed class MethodInvocationFeatureCheckerService : IMethodInvocationFeatureCheckerService
{
    private readonly IFeatureManager _featureManager;

    public MethodInvocationFeatureCheckerService(IFeatureManager featureManager)
    {
        _featureManager = featureManager;
    }

    public async Task CheckAsync(MethodInvocationFeatureCheckerContext context)
    {
        if (_IsFeatureCheckDisabled(context))
        {
            return;
        }

        foreach (var requiresFeatureAttribute in _GetRequiredFeatureAttributes(context.Method))
        {
            await _featureManager.EnsureEnabledAsync(requiresFeatureAttribute.IsAnd, requiresFeatureAttribute.Features);
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
            attributes = attributes.Union(
                methodInfo.DeclaringType!.GetCustomAttributes(inherit: true).OfType<RequiresFeatureAttribute>()
            );
        }

        return attributes;
    }
}
