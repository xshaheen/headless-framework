// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Features.Filters;

/// <summary>
/// This attribute can be used on a class/method to declare that given class/method is available
/// only if required feature(s) are enabled.
/// </summary>
/// <remarks>Creates a new instance of <see cref="RequiresFeatureAttribute"/> class.</remarks>
/// <param name="features">A list of features to be checked if they are enabled</param>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresFeatureAttribute(params string[]? features) : Attribute
{
    /// <summary>A list of features to be checked if they are enabled.</summary>
    public string[] Features { get; } = features ?? [];

    /// <summary>
    /// If this property is set to true, all of the <see cref="Features"/> must be enabled.
    /// If it's false, at least one of the <see cref="Features"/> must be enabled.
    /// Default: false.
    /// </summary>
    public bool IsAnd { get; set; }
}
