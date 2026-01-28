// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Validators;
using NJsonSchema;
using NJsonSchema.Generation;

namespace Headless.Api.SchemaProcessors.FluentValidation.Models;

public sealed class RuleContext(
    SchemaProcessorContext processorContext,
    string propertyKey,
    IPropertyValidator propertyValidator
)
{
    public SchemaProcessorContext ProcessorContext { get; } = processorContext;

    public string PropertyKey { get; } = propertyKey;

    public IPropertyValidator PropertyValidator { get; } = propertyValidator;

    public JsonSchema PropertySchema =>
        ProcessorContext.Schema.IsObject ? ProcessorContext.Schema.Properties[PropertyKey] : ProcessorContext.Schema;
}
