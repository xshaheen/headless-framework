// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features.Filters;

/// <summary>
/// Marks a method to skip any feature-check filter that would otherwise run on the containing class.
/// Apply this to action methods that should be accessible regardless of whether a feature is enabled.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Method)]
public sealed class DisableFeatureCheckAttribute : Attribute;
