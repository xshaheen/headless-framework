// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Sql;

/// <summary>One column of a row key, matched against a parameter.</summary>
/// <param name="Column">The quoted column.</param>
/// <param name="Parameter">The parameter name, without its prefix.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct SqlKeyColumn(string Column, string Parameter);
