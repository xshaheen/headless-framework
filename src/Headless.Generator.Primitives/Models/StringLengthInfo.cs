// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.SourceGenerators;

namespace Headless.Generator.Primitives.Models;

/// <summary>String length validation info.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct StringLengthInfo(int MinLength, int MaxLength, bool ShouldValidate);
