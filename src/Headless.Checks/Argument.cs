// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Checks;

/// <summary>
/// Argument guard clauses that throw standard BCL exceptions on invalid input.
/// All members are inlined and step-through-hidden so they disappear from debugger stack traces.
/// </summary>
/// <remarks>
/// Exception type by check category:
/// <list type="bullet">
///   <item><description><see cref="ArgumentNullException"/> — null checks (<c>IsNotNull</c>, <c>IsNotNullOrEmpty</c>, <c>HasNoNulls</c>).</description></item>
///   <item><description><see cref="ArgumentException"/> — empty / whitespace / format checks (<c>IsNotEmpty</c>, <c>IsNotNullOrWhiteSpace</c>, <c>IsDefault</c>, <c>IsOneOf</c>, <c>Range</c>, <c>HasNoNull*Elements</c>).</description></item>
///   <item><description><see cref="ArgumentOutOfRangeException"/> — numeric / comparable range checks (<c>IsPositive</c>, <c>IsNegative</c>, <c>IsInclusiveBetween</c>, etc.).</description></item>
/// </list>
/// Use <see cref="Ensure"/> for runtime state assertions that are not about caller arguments.
/// The auto-captured <c>paramName</c> parameter is the caller's argument expression and becomes the thrown
/// exception's <see cref="ArgumentException.ParamName"/>; unlike <see cref="Ensure"/>'s <c>expression</c>, it names
/// the offending parameter rather than describing a checked condition.
/// </remarks>
[PublicAPI]
public static partial class Argument;
