// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Generator.Primitives.Helpers;

internal static class AbstractionConstants
{
    internal const string Interface = "IPrimitive";
    internal const string NamespacePart1 = "Framework";
    internal const string NamespacePart2 = "Generator";
    internal const string NamespacePart3 = "Primitives";
    internal const string Namespace = $"{NamespacePart1}.{NamespacePart2}.{NamespacePart3}";

    internal const string SerializationFormatAttribute = "SerializationFormatAttribute";
    internal const string SerializationFormatAttributeFullName = $"{Namespace}.{SerializationFormatAttribute}";

    internal const string SupportedOperationsAttribute = "SupportedOperationsAttribute";
    internal const string SupportedOperationsAttributeFullName = $"{Namespace}.{SupportedOperationsAttribute}";

    internal const string StringLengthAttribute = "StringLengthAttribute";
    internal const string StringLengthAttributeFullName = $"{Namespace}.{StringLengthAttribute}";

    internal const string PrimitiveAssemblyAttribute = "PrimitiveAssemblyAttribute";
    internal const string PrimitiveAssemblyAttributeFullName = $"{Namespace}.{PrimitiveAssemblyAttribute}";
}
