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
    IPropertyValidator propertyValidator,
    bool isCollectionRule = false
)
{
    /// <summary>The NSwag schema processor context for the type that owns this property.</summary>
    public SchemaProcessorContext ProcessorContext { get; } = processorContext;

    /// <summary>The schema-level property key (typically the camelCase property name).</summary>
    public string PropertyKey { get; } = propertyKey;

    /// <summary>The FluentValidation property validator that matched <see cref="FluentValidationRule.Matches"/>.</summary>
    public IPropertyValidator PropertyValidator { get; } = propertyValidator;

    /// <summary>
    /// Whether the validator was declared with <c>RuleForEach</c> and therefore constrains the elements
    /// of a collection property rather than the property itself.
    /// </summary>
    public bool IsCollectionRule { get; } = isCollectionRule;

    /// <summary>
    /// The <c>JsonSchema</c> a rule should mutate. When the parent schema is an object this is
    /// <c>ProcessorContext.Schema.Properties[PropertyKey]</c>; otherwise it is the schema itself (for
    /// types used as query-parameter shapes). For a collection rule it is the property's <c>items</c>
    /// schema, because the constraint applies to each element.
    /// </summary>
    /// <remarks>
    /// FluentValidation reports a <c>RuleForEach(x =&gt; x.Scores).GreaterThan(0)</c> rule under the
    /// property name <c>Scores</c>, exactly like a non-collection rule. Without the redirect the bound
    /// lands on the array schema, where <c>minimum</c>, <c>maxLength</c>, and <c>pattern</c> have no
    /// meaning — JSON Schema constrains array size with <c>minItems</c>/<c>maxItems</c> instead — and
    /// strict client generators reject the resulting document.
    /// </remarks>
    public JsonSchema PropertySchema
    {
        get
        {
            var schema = ProcessorContext.Schema.IsObject
                ? ProcessorContext.Schema.Properties[PropertyKey]
                : ProcessorContext.Schema;

            return IsCollectionRule && schema.Item is not null ? schema.Item : schema;
        }
    }
}
