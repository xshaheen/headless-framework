// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Generator.Primitives;

/// <summary>Represents an attribute applied to assemblies to indicate that they're part of a Primitives assembly.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class PrimitiveAssemblyAttribute : Attribute;
