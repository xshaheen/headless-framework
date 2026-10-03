// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Headless.Checks;

namespace Headless.Security;

/// <summary>A named parameter of a <see cref="PhcString" />, such as <c>m=19456</c>.</summary>
/// <param name="Name">The parameter name.</param>
/// <param name="Value">The parameter value, as written.</param>
[PublicAPI]
public readonly record struct PhcParameter(string Name, string Value);
