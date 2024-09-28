// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Generator.Primitives.Helpers;

internal static class AbstractionConstants
{
    internal const string Interface = "IPrimitive";
    internal const string Namespace = "Framework.Generator.Primitives";

    internal const string SerializationFormatAttribute = "SerializationFormatAttribute";
    internal const string SerializationFormatAttributeFullName = $"{Namespace}.{SerializationFormatAttribute}";

    internal const string SupportedOperationsAttribute = "SupportedOperationsAttribute";
    internal const string SupportedOperationsAttributeFullName = $"{Namespace}.{SupportedOperationsAttribute}";

    internal const string StringLengthAttribute = "StringLengthAttribute";
    internal const string StringLengthAttributeFullName = $"{Namespace}.{StringLengthAttribute}";

    internal const string PrimitiveAssemblyAttribute = "PrimitiveAssemblyAttribute";
    internal const string PrimitiveAssemblyAttributeFullName = $"{Namespace}.{PrimitiveAssemblyAttribute}";
}
