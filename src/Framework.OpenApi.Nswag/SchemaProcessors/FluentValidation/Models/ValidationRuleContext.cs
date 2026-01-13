// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Framework.Api.SchemaProcessors.FluentValidation.Models;

public readonly record struct ValidationRuleContext(IValidationRule ValidationRule);
