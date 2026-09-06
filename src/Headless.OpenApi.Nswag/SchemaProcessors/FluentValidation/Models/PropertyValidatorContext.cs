// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Validators;

namespace Headless.OpenApi.Nswag.SchemaProcessors.FluentValidation.Models;

/// <summary>
/// Pairs a property validator with the shape of the rule that declared it.
/// </summary>
/// <param name="Validator">The FluentValidation property validator.</param>
/// <param name="IsCollectionRule">
/// <see langword="true"/> when the validator came from a <c>RuleForEach</c> rule and therefore constrains
/// each element of the collection rather than the collection itself.
/// </param>
public readonly record struct PropertyValidatorContext(IPropertyValidator Validator, bool IsCollectionRule);
