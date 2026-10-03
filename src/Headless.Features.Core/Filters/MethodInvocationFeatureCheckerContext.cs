// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Headless.Features.Values;

namespace Headless.Features.Filters;

/// <summary>Carries the reflection metadata needed to check feature requirements for a single method invocation.</summary>
/// <param name="Method">The method being invoked.</param>
public sealed record MethodInvocationFeatureCheckerContext(MethodInfo Method);
