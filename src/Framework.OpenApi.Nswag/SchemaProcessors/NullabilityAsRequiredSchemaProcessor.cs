// Copyright (c) Mahmoud Shaheen. All rights reserved.

using NJsonSchema;
using NJsonSchema.Generation;

namespace Framework.OpenApi.Nswag.SchemaProcessors;

/// <summary>
/// Swagger <see cref="ISchemaProcessor"/> that uses the nullability annotations to set the required properties.
/// </summary>
public sealed class NullabilityAsRequiredSchemaProcessor : ISchemaProcessor
{
    public void Process(SchemaProcessorContext context)
    {
        if (!context.Schema.IsObject || context.Schema.Properties.Count == 0)
        {
            return;
        }

        foreach (var (name, property) in context.Schema.Properties)
        {
            if (
                !property.IsNullable(SchemaType.OpenApi3)
                && !context.Schema.RequiredProperties.Contains(name, StringComparer.Ordinal)
            )
            {
                context.Schema.RequiredProperties.Add(name);
            }
        }
    }
}
