// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.OpenApi.Nswag.SchemaProcessors.FluentValidation.Models;

/// <summary>
/// Lightweight wrapper that pairs an <c>IValidationRule</c> with its enumeration position when iterating
/// a validator's rule set during schema generation.
/// </summary>
/// <param name="ValidationRule">The FluentValidation rule being inspected.</param>
public readonly record struct ValidationRuleContext(IValidationRule ValidationRule);
