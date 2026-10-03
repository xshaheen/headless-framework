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
