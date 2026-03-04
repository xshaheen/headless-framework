// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Namotion.Reflection;
using NJsonSchema.Generation;

namespace Headless.Api.SchemaProcessors;

/// <summary>
/// Fixes nullability detection for generic type parameters (e.g., <c>T?</c> in <c>DataEnvelope&lt;T&gt;</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Problem:</b> NSwag/NJsonSchema cannot detect nullability annotations on generic type
/// arguments. Given <c>DataEnvelope&lt;string?&gt;</c>, the property of type <c>T</c> is not
/// marked as nullable in the generated OpenAPI schema because <c>string?</c> and <c>string</c>
/// are the same CLR type at runtime — the nullable annotation only exists in metadata at the
/// usage site, which NJsonSchema does not propagate to the generic type's property schemas.
/// </para>
/// <para>
/// <b>Solution:</b> This processor inspects <see cref="ContextualType.GenericArguments"/>
/// (from Namotion.Reflection), which preserves the nullable annotation context. For each schema
/// property whose declaring type is a generic type parameter, it checks whether the corresponding
/// generic argument is annotated as nullable and sets <c>IsNullableRaw = true</c> on the property
/// schema accordingly.
/// </para>
/// <para>
/// <b>Conflict resolution:</b> When the same generic type is instantiated with both nullable and
/// non-nullable arguments (e.g., <c>Wrapper&lt;string?&gt;</c> and <c>Wrapper&lt;string&gt;</c>),
/// they share a single runtime type and schema definition. The nullable variant wins because the
/// processor only ever sets <c>IsNullableRaw = true</c>, never <c>false</c> — so once any
/// instantiation marks a property as nullable, subsequent non-nullable instantiations leave it
/// unchanged.
/// </para>
/// <para>
/// <b>Registration order:</b> Must be registered <b>before</b>
/// <see cref="NullabilityAsRequiredSchemaProcessor"/> so that nullability is resolved before
/// required properties are determined.
/// </para>
/// </remarks>
public sealed class GenericNullabilitySchemaProcessor : ISchemaProcessor
{
    public void Process(SchemaProcessorContext context)
    {
        if (
            !context.ContextualType.Type.IsConstructedGenericType
            || !context.Schema.IsObject
            || context.Schema.Properties.Count == 0
        )
        {
            return;
        }

        var contextualArgs = context.ContextualType.GenericArguments;
        var genericDef = context.ContextualType.Type.GetGenericTypeDefinition();

        foreach (var (propertyName, propertySchema) in context.Schema.Properties)
        {
            // Schema property names may be camelCase (System.Text.Json default)
            // while CLR property names are PascalCase — IgnoreCase bridges the gap.
            var defProperty = genericDef.GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
            );

            if (defProperty?.PropertyType is not { IsGenericParameter: true } genericParam)
            {
                continue;
            }

            var position = genericParam.GenericParameterPosition;

            if (position >= contextualArgs.Length)
            {
                continue;
            }

            if (contextualArgs[position].Nullability == Nullability.Nullable)
            {
                propertySchema.IsNullableRaw = true;
            }
        }
    }
}
