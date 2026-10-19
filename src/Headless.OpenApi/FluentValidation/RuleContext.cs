// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Validators;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Carries what a <see cref="FluentValidationRule.Apply"/> delegate needs: the schema that owns the property, the
/// property key, and the matched FluentValidation validator.
/// </summary>
/// <param name="schema">
/// The schema of the type that owns the property, or the parameter schema itself when a validator constrains one
/// property bound as a query, route, or header parameter.
/// </param>
/// <param name="propertyKey">The schema property key (typically the camelCase property name).</param>
/// <param name="propertyValidator">The FluentValidation property validator that matched the rule.</param>
/// <param name="isCollectionRule">
/// Whether the validator was declared with <c>RuleForEach</c> and so constrains each element of a collection.
/// </param>
public sealed class RuleContext(
    OpenApiSchema schema,
    string propertyKey,
    IPropertyValidator propertyValidator,
    bool isCollectionRule = false
)
{
    /// <summary>The schema of the type that owns the property, or the parameter schema.</summary>
    public OpenApiSchema Schema { get; } = schema;

    /// <summary>The schema property key (typically the camelCase property name).</summary>
    public string PropertyKey { get; } = propertyKey;

    /// <summary>The FluentValidation property validator that matched <see cref="FluentValidationRule.Matches"/>.</summary>
    public IPropertyValidator PropertyValidator { get; } = propertyValidator;

    /// <summary>
    /// Whether the validator was declared with <c>RuleForEach</c> and so constrains the elements of a collection
    /// property rather than the property itself.
    /// </summary>
    public bool IsCollectionRule { get; } = isCollectionRule;

    /// <summary>Whether <see cref="Schema"/> is an object schema whose property <see cref="PropertyKey"/> is constrained.</summary>
    public bool IsObjectSchema => Schema.Properties?.ContainsKey(PropertyKey) == true;

    /// <summary>
    /// The schema a rule should constrain: <c>Schema.Properties[PropertyKey]</c> for an object schema, otherwise
    /// <see cref="Schema"/> itself. For a collection rule it is that schema's <c>items</c>, because the constraint
    /// applies to each element. <see langword="null"/> when that schema is a reference, which a rule cannot change
    /// without changing every other use of the referenced component.
    /// </summary>
    /// <remarks>
    /// FluentValidation reports a <c>RuleForEach(x =&gt; x.Scores).GreaterThan(0)</c> rule under the property name
    /// <c>Scores</c>, exactly like a non-collection rule. Without the redirect the bound lands on the array schema,
    /// where <c>minimum</c>, <c>maxLength</c>, and <c>pattern</c> have no meaning, and strict client generators
    /// reject the document.
    /// </remarks>
    public OpenApiSchema? PropertySchema
    {
        get
        {
            var target = IsObjectSchema ? Schema.Properties![PropertyKey] : Schema;

            if (IsCollectionRule && target is OpenApiSchema { Items: { } items })
            {
                target = items;
            }

            return target as OpenApiSchema;
        }
    }

    /// <summary>
    /// Removes <see langword="null"/> from the type and alternatives of the schema <see cref="PropertySchema"/> resolves to. A
    /// nullable reference (<c>oneOf: [null, $ref]</c>) collapses to the reference itself.
    /// </summary>
    public void RemovePropertyNullability()
    {
        var target = IsObjectSchema ? Schema.Properties![PropertyKey] : Schema;

        if (IsCollectionRule && target is OpenApiSchema { Items: { } items } array)
        {
            array.Items = OpenApiSchemaNullability.RemoveNullability(items);

            return;
        }

        if (IsObjectSchema)
        {
            var properties = Schema.Properties!;
            properties[PropertyKey] = OpenApiSchemaNullability.RemoveNullability(properties[PropertyKey]);

            return;
        }

        OpenApiSchemaNullability.RemoveNullability(Schema);
    }
}
