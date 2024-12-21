// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Framework.OpenApi.Nswag.SchemaProcessors.FluentValidation.Models;

public readonly record struct ValidationRuleContext(IValidationRule ValidationRule, bool IsCollectionRule);
