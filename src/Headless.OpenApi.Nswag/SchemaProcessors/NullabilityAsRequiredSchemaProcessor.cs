// Copyright (c) Mahmoud Shaheen. All rights reserved.

using NJsonSchema.Generation;

namespace Headless.OpenApi.Nswag.SchemaProcessors;

/// <summary>
/// NSwag schema processor that promotes non-nullable object properties to the OpenAPI
/// <c>required</c> list, so that clients know those properties are always present in responses.
/// </summary>
/// <remarks>
/// Must be registered <b>after</b> <see cref="GenericNullabilitySchemaProcessor"/> so that generic
/// type parameter nullability is resolved before the required-property decision is made.
/// Non-object schemas (primitives, arrays) are passed through unchanged.
/// </remarks>
public sealed class NullabilityAsRequiredSchemaProcessor : ISchemaProcessor
{
    /// <summary>
    /// Adds non-nullable properties of the current object schema to the <c>required</c> set.
    /// </summary>
    /// <param name="context">The NSwag schema processor context for the type being processed.</param>
    public void Process(SchemaProcessorContext context)
    {
        context.Schema.NormalizeNullableAsRequired();
    }
}
