// Copyright (c) Mahmoud Shaheen. All rights reserved.

using NJsonSchema;
using NJsonSchema.Generation;

namespace Framework.OpenApi.Nswag.SchemaProcessors;

[PublicAPI]
public static class SchemaProcessorContextExtensions
{
    public static JsonSchema NormalizeNullableAsRequired(this JsonSchema schema)
    {
        if (!schema.IsObject || schema.Properties.Count == 0)
        {
            return schema;
        }

        foreach (var (name, property) in schema.Properties)
        {
            if (
                !property.IsNullable(SchemaType.OpenApi3)
                && !schema.RequiredProperties.Contains(name, StringComparer.Ordinal)
            )
            {
                schema.RequiredProperties.Add(name);
            }
        }

        return schema;
    }
}
