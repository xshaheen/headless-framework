// Copyright (c) Mahmoud Shaheen. All rights reserved.
// Polyfills for C# 9+ features in netstandard2.0

using System.ComponentModel;

namespace System.Runtime.CompilerServices;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.
/// <summary>
/// Reserved for compiler use when tracking init-only setter metadata.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit;

/// <summary>
/// Specifies that a type has required members or that a member is required.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property,
    AllowMultiple = false,
    Inherited = false
)]
[EditorBrowsable(EditorBrowsableState.Never)]
internal sealed class RequiredMemberAttribute : Attribute;

/// <summary>
/// Indicates that a language feature is required by the compiler.
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
