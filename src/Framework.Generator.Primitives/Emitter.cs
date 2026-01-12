// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using Framework.Generator.Primitives.Extensions;
using Framework.Generator.Primitives.Helpers;
using Framework.Generator.Primitives.Models;
using Framework.Generator.Primitives.Shared;
using Microsoft.CodeAnalysis;

namespace Framework.Generator.Primitives;

/// <summary>A static class responsible for executing the generation of code for primitive types.</summary>
internal static class Emitter
{
    /// <summary>Executes the generation of primitives based on the provided parameters.</summary>
    /// <param name="context">The source production context.</param>
    /// <param name="typesToGenerate">The list of primitive type infos to generate.</param>
    /// <param name="assemblyName">The name of the assembly.</param>
    /// <param name="globalOptions">The global options for primitive generation.</param>
    internal static void Execute(
        in SourceProductionContext context,
        in ImmutableArray<PrimitiveTypeInfo?> typesToGenerate,
        in string assemblyName,
        in PrimitiveGlobalOptions globalOptions
    )
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        if (typesToGenerate.IsDefaultOrEmpty)
        {
            return;
        }

        var swaggerTypes = new List<GeneratorData>(typesToGenerate.Length);
        var efValueConverterTypes = new List<GeneratorData>(typesToGenerate.Length);
        var dapperConverterTypes = new List<GeneratorData>(typesToGenerate.Length);

        try
        {
            foreach (var typeInfo in typesToGenerate)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                if (typeInfo is null)
                {
                    continue;
                }

                var info = typeInfo.Value;

                // Check for type mismatch between primitive and underlying type
                if (info.UnderlyingTypeIsValueType && !info.IsValueType)
                {
                    context.ReportDiagnostic(
                        DiagnosticHelper.TypeShouldBeValueType(
                            info.ClassName,
                            info.UnderlyingTypeFriendlyName,
                            Location.None
                        )
                    );
                }
                else if (!info.UnderlyingTypeIsValueType && info.IsValueType)
                {
                    context.ReportDiagnostic(
                        DiagnosticHelper.TypeShouldBeReferenceType(
                            info.ClassName,
                            info.UnderlyingTypeFriendlyName,
                            Location.None
                        )
                    );
                }

                var generatorData = GeneratorData.FromTypeInfo(info, globalOptions);

                if (!_ProcessType(generatorData, globalOptions, context))
                {
                    continue;
                }

                if (globalOptions.GenerateJsonConverters)
                {
                    context.AddJsonConverter(generatorData);
                }

                if (globalOptions.GenerateTypeConverters)
                {
                    context.AddTypeConverter(generatorData);
                }

                if (globalOptions.GenerateEntityFrameworkValueConverters)
                {
                    efValueConverterTypes.Add(generatorData);
                    context.AddEntityFrameworkValueConverter(generatorData);
                }

                if (globalOptions.GenerateDapperConverters)
                {
                    dapperConverterTypes.Add(generatorData);
                    context.AddDapperTypeHandlerConverter(generatorData);
                }

                if (globalOptions.GenerateSwashbuckleSwaggerConverters || globalOptions.GenerateNswagSwaggerConverters)
                {
                    swaggerTypes.Add(generatorData);
                }
            }

            // Add helpers
            var addAssemblyAttribute = true;

            if (globalOptions.GenerateSwashbuckleSwaggerConverters && swaggerTypes.Count > 0)
            {
                context.AddSwashbuckleSwaggerMappingsHelper(assemblyName, swaggerTypes, addAssemblyAttribute);
                addAssemblyAttribute = false;
            }

            if (globalOptions.GenerateNswagSwaggerConverters && swaggerTypes.Count > 0)
            {
                context.AddNswagSwaggerMappingsHelper(assemblyName, swaggerTypes, addAssemblyAttribute);
                addAssemblyAttribute = false;
            }

            if (globalOptions.GenerateEntityFrameworkValueConverters && efValueConverterTypes.Count > 0)
            {
                context.AddEntityFrameworkValueConvertersHelper(
                    assemblyName,
                    efValueConverterTypes,
                    addAssemblyAttribute
                );
                addAssemblyAttribute = false;
            }

            if (globalOptions.GenerateDapperConverters && dapperConverterTypes.Count > 0)
            {
                context.AddDapperTypeHandlersHelper(assemblyName, dapperConverterTypes, addAssemblyAttribute);
            }
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(DiagnosticHelper.GeneralError(Location.None, ex));
        }
    }

    /// <summary>
    /// Processes the generation of code for a specific data type.
    /// </summary>
    /// <param name="data">The GeneratorData containing information about the data type.</param>
    /// <param name="options">The PrimitiveGlobalOptions for code generation.</param>
    /// <param name="context">The SourceProductionContext for reporting diagnostics.</param>
    /// <returns>True if the code generation process was successful, otherwise, false.</returns>
    private static bool _ProcessType(
        GeneratorData data,
        PrimitiveGlobalOptions options,
        SourceProductionContext context
    )
    {
        var builder = new SourceCodeBuilder();
        var isSuccess = _ProcessConstructor(data, builder);

        if (!isSuccess)
        {
            return false;
        }

        context.AddPrimateImplementation(data, builder.ToString(), options);

        return true;
    }

    /// <summary>Processes the constructor for a specified class.</summary>
    /// <param name="data">The GeneratorData containing information about the data type.</param>
    /// <param name="builder">The SourceCodeBuilder for generating source code.</param>
    /// <returns>A boolean indicating whether the constructor processing was successful.</returns>
    private static bool _ProcessConstructor(GeneratorData data, SourceCodeBuilder builder)
    {
        // Note: Constructor validation (HasDefaultConstructor, HasConstructorWithParam) was previously done
        // using symbol analysis. Since we can't do that without symbols, we generate the code and
        // rely on the C# compiler to catch any constructor conflicts.
        // The generator will still produce valid code, and any conflicts will be reported by the compiler.

        var underlyingTypeName = data.PrimitiveTypeFriendlyName;

        builder.AppendLine(
            $"private {underlyingTypeName} _valueOrThrow => _isInitialized ? _value : throw new InvalidPrimitiveValueException(\"The domain value has not been initialized\", this);"
        );

        builder.NewLine();

        builder.AppendDebuggerBrowsableNeverAttribute();
        builder.AppendLine($"private readonly {underlyingTypeName} _value;");
        builder.NewLine();

        builder.AppendDebuggerBrowsableNeverAttribute();
        builder.AppendLine("private readonly bool _isInitialized;");
        builder.NewLine();

        builder.AppendSummary(
            $"Initializes a new instance of the <see cref=\"{data.ClassName}\"/> class by validating the specified <see cref=\"{underlyingTypeName}\"/> value using <see cref=\"Validate\"/> static method."
        );

        builder.AppendParamDescription("value", "The value to be validated.");

        builder.Append($"public {data.ClassName}({underlyingTypeName} value) : this(value, true) {{ }}").NewLine(2);

        builder.AppendLine($"private {data.ClassName}({underlyingTypeName} value, bool validate)").OpenBracket();
        builder.AppendLine("if (validate)").OpenBracket();

        if (data.UnderlyingType == PrimitiveUnderlyingType.String)
        {
            _AddStringLengthAttributeValidation(data, builder);
        }

        builder.AppendLine("ValidateOrThrow(value);");
        builder.CloseBracket().AppendLine("_value = value;").AppendLine("_isInitialized = true;").CloseBracket();

        builder.NewLine();

        var primitiveTypeIsValueType = data.PrimitiveTypeIsValueType;

        if (!primitiveTypeIsValueType)
        {
            builder.AppendNullableDisable();
        }

        builder.AppendLine("#pragma warning disable AL1003 // Should not have non obsolete empty constructors.");

        builder
            .AppendLine("[Obsolete(\"Primitive cannot be created using empty Constructor\", true)]")
            .Append("public ")
            .Append(data.ClassName)
            .AppendLine("() { }");

        builder.AppendLine("#pragma warning restore AL1003");

        if (!primitiveTypeIsValueType)
        {
            builder.AppendNullableEnable();
        }

        return true;
    }

    private static void _AddStringLengthAttributeValidation(GeneratorData data, SourceCodeBuilder sb)
    {
        if (data.StringLengthAttributeValidation is null)
        {
            return;
        }

        var (minValue, maxValue) = data.StringLengthAttributeValidation.Value;
        var hasMinValue = minValue >= 0;
        var hasMaxValue = maxValue != int.MaxValue;

        if (!hasMinValue && !hasMaxValue)
        {
            return;
        }

        var minValueText = minValue.ToString(CultureInfo.InvariantCulture);
        var maxValueText = maxValue.ToString(CultureInfo.InvariantCulture);

        sb.Append("if (value.Length is ")
            .AppendIf(hasMinValue, $"< {minValueText}")
            .AppendIf(hasMinValue && hasMaxValue, " or ")
            .AppendIf(hasMaxValue, $"> {maxValueText}")
            .AppendLine(")")
            .AppendLine(
                $"\tthrow new InvalidPrimitiveValueException(\"String length is out of range {minValueText}..{maxValueText}\", this);"
            )
            .NewLine();
    }
}
