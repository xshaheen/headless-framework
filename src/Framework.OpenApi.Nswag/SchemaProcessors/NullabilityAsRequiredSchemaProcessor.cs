// Copyright (c) Mahmoud Shaheen. All rights reserved.

using NJsonSchema.Generation;

namespace Framework.OpenApi.Nswag.SchemaProcessors;

/// <summary>
/// Swagger <see cref="ISchemaProcessor"/> that uses the nullability annotations to set the required properties.
/// </summary>
public sealed class NullabilityAsRequiredSchemaProcessor : ISchemaProcessor
{
    public void Process(SchemaProcessorContext context)
    {
        context.Schema.NormalizeNullableAsRequired();
    }
}
