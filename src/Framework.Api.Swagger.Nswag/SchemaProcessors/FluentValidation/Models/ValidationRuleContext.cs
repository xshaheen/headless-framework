// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using FluentValidation;

namespace Framework.Api.Swagger.Nswag.SchemaProcessors.FluentValidation.Models;

public readonly record struct ValidationRuleContext(IValidationRule ValidationRule, bool IsCollectionRule);
