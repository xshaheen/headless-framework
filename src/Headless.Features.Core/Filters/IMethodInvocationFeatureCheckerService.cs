// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
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
    /// <summary>
    /// A method's attributes never change, so the reflection result is memoized for the process rather than
    /// re-materialized on every intercepted invocation (the previous shape walked the method's attributes
    /// twice and the declaring type's once, per call). Keyed by <see cref="MethodInfo"/>, whose equality is
    /// handle-based, and bounded by the set of intercepted methods in the loaded assemblies.
    /// </summary>
    private static readonly ConcurrentDictionary<MethodInfo, FeatureRequirements> _RequirementsCache = new();

    /// <inheritdoc/>
    public async Task CheckAsync(MethodInvocationFeatureCheckerContext context)
    {
        var requirements = _RequirementsCache.GetOrAdd(context.Method, static method => _ResolveRequirements(method));

        if (requirements.IsCheckDisabled)
        {
            return;
        }

        foreach (var requiresFeatureAttribute in requirements.Required)
        {
            await featureManager
                .EnsureEnabledAsync(requiresFeatureAttribute.IsAnd, requiresFeatureAttribute.Features)
                .ConfigureAwait(false);
        }
    }

    private static FeatureRequirements _ResolveRequirements(MethodInfo methodInfo)
    {
        var methodAttributes = methodInfo.GetCustomAttributes(inherit: true);

        foreach (var attribute in methodAttributes)
        {
            if (attribute is DisableFeatureCheckAttribute)
            {
                return new FeatureRequirements(IsCheckDisabled: true, Required: []);
            }
        }

        var attributes = methodAttributes.OfType<RequiresFeatureAttribute>();

        if (methodInfo.IsPublic)
        {
            var requiresFeatureAttributes = methodInfo
                .DeclaringType!.GetCustomAttributes(inherit: true)
                .OfType<RequiresFeatureAttribute>();

            // Union, not Concat: Attribute equality is value-based, so a requirement declared identically on
            // both the method and its declaring type is applied once.
            attributes = attributes.Union(requiresFeatureAttributes);
        }

        return new FeatureRequirements(IsCheckDisabled: false, Required: [.. attributes]);
    }

    private readonly record struct FeatureRequirements(bool IsCheckDisabled, RequiresFeatureAttribute[] Required);
}
