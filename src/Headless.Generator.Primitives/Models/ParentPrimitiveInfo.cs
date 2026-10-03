// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.SourceGenerators;

namespace Headless.Generator.Primitives.Models;

/// <summary>Info about a parent primitive type in the inheritance chain.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ParentPrimitiveInfo(
    string Name,
    string Namespace,
    string FriendlyName,
    bool IsValueType
);
