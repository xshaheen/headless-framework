// Copyright (c) Mahmoud Shaheen. All rights reserved.
// Polyfills for C# 9+ features in netstandard2.0

using System.ComponentModel;

namespace System.Runtime.CompilerServices;

/// <summary>
/// Indicates that a feature is required by the compiler.
/// </summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
internal sealed class CompilerFeatureRequiredAttribute(string featureName) : Attribute
{
    public string FeatureName { get; } = featureName;

    public bool IsOptional { get; init; }

    public const string RefStructs = nameof(RefStructs);

    public const string RequiredMembers = nameof(RequiredMembers);
}
