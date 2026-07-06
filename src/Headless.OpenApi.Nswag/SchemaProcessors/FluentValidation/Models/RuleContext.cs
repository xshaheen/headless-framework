// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Validators;
using NJsonSchema;
using NJsonSchema.Generation;

namespace Headless.OpenApi.Nswag.SchemaProcessors.FluentValidation.Models;

/// <summary>
/// Carries the context passed to a <see cref="FluentValidationRule.Apply"/> delegate, giving it access
/// to the NSwag schema processor context, the property being validated, and the matched FluentValidation
/// validator instance.
/// </summary>
public sealed class RuleContext(
    SchemaProcessorContext processorContext,
    string propertyKey,
    IPropertyValidator propertyValidator
)
{
    /// <summary>The NSwag schema processor context for the type that owns this property.</summary>
    public SchemaProcessorContext ProcessorContext { get; } = processorContext;

    /// <summary>The schema-level property key (typically the camelCase property name).</summary>
    public string PropertyKey { get; } = propertyKey;

    /// <summary>The FluentValidation property validator that matched <see cref="FluentValidationRule.Matches"/>.</summary>
    public IPropertyValidator PropertyValidator { get; } = propertyValidator;

    /// <summary>
    /// The <c>JsonSchema</c> for the individual property. When the parent schema is an object this is
    /// <c>ProcessorContext.Schema.Properties[PropertyKey]</c>; otherwise it is the schema itself (for
    /// types used as query-parameter shapes).
    /// </summary>
    public JsonSchema PropertySchema =>
        ProcessorContext.Schema.IsObject ? ProcessorContext.Schema.Properties[PropertyKey] : ProcessorContext.Schema;
}
