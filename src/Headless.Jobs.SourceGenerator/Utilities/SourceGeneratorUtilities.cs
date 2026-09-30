// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Headless.Jobs.SourceGenerator.Utilities;

/// <summary>
/// Utility methods for source generation operations.
/// </summary>
internal static class SourceGeneratorUtilities
{
    /// <summary>
    /// Converts the first letter of a string to lowercase.
    /// </summary>
    public static string FirstLetterToLower(string input)
    {
        if (string.IsNullOrEmpty(input) || char.IsLower(input[0]))
        {
            return input;
        }

        return char.ToLowerInvariant(input[0]) + input.Substring(1);
    }

    /// <summary>
    /// Determines if a method is awaitable (returns <c>Task</c> or <c>Task&lt;T&gt;</c>).
    /// </summary>
    public static bool IsMethodAwaitable(MethodDeclarationSyntax methodDeclaration)
    {
        var returnType = methodDeclaration.ReturnType.ToString();
        return returnType.StartsWith("Task", StringComparison.Ordinal);
    }

    public static bool IsJobFunctionAttribute(AttributeData attribute) =>
        string.Equals(
            attribute.AttributeClass?.ToDisplayString(),
            SourceGeneratorConstants.JobFunctionAttributeMetadataName,
            StringComparison.Ordinal
        );

    public static bool IsJobsConstructorAttribute(AttributeData attribute)
    {
        var attributeClass = attribute.AttributeClass;
        if (attributeClass == null)
        {
            return false;
        }

        var attributeName = attributeClass.Name;
        var fullName = attributeClass.ToDisplayString();

        return string.Equals(attributeName, "JobsConstructorAttribute", StringComparison.Ordinal)
            || string.Equals(attributeName, "JobsConstructor", StringComparison.Ordinal)
            || string.Equals(fullName, "Headless.Jobs.Base.JobsConstructorAttribute", StringComparison.Ordinal)
            || string.Equals(fullName, "Headless.Jobs.Base.JobsConstructor", StringComparison.Ordinal);
    }

    public static bool IsFromKeyedServicesAttribute(AttributeData attribute)
    {
        var name = attribute.AttributeClass?.Name;
        var fullName = attribute.AttributeClass?.ToDisplayString();
        return string.Equals(name, SourceGeneratorConstants.FromKeyedServicesAttributeName, StringComparison.Ordinal)
            || string.Equals(name, "FromKeyedServices", StringComparison.Ordinal)
            || string.Equals(
                fullName,
                "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute",
                StringComparison.Ordinal
            )
            || fullName?.EndsWith(SourceGeneratorConstants.FromKeyedServicesAttributeName, StringComparison.Ordinal)
                == true;
    }

    /// <summary>
    /// Gets the service key from a FromKeyedServicesAttribute as a C# expression.
    /// </summary>
    public static string? GetServiceKey(AttributeData keyedServiceAttribute)
    {
        if (keyedServiceAttribute.ConstructorArguments.Length > 0)
        {
            var keyArg = keyedServiceAttribute.ConstructorArguments[0];
            if (keyArg.Value is string stringKey)
            {
                return $"\"{stringKey}\"";
            }

            if (keyArg.Value != null)
            {
                return _FormatServiceKeyValue(keyArg.Value);
            }
        }

        return null;
    }

    private static string _FormatServiceKeyValue(object value)
    {
        return value switch
        {
            double d => d.ToString("G", CultureInfo.InvariantCulture) + "D",
            float f => f.ToString("G", CultureInfo.InvariantCulture) + "F",
            decimal m => m.ToString("G", CultureInfo.InvariantCulture) + "M",
            long l => l.ToString(CultureInfo.InvariantCulture) + "L",
            uint ui => ui.ToString(CultureInfo.InvariantCulture) + "U",
            ulong ul => ul.ToString(CultureInfo.InvariantCulture) + "UL",
            char c => $"'{c}'",
            bool b => b ? "true" : "false",
            _ => value.ToString(),
        };
    }
}
