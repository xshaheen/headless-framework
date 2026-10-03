// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Generator.Primitives;

/// <summary>Represents an attribute applied to assemblies to indicate that they're part of a Primitives assembly.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class PrimitiveAssemblyAttribute : Attribute;
