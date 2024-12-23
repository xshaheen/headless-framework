// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Generator.Primitives.Extensions;
using Framework.Generator.Primitives.Models;
using Framework.Generator.Primitives.Shared;

namespace Framework.Generator.Primitives.Helpers;

/// <summary>A helper class providing methods for generating code related to Swagger, TypeConverter, JsonConverter, and other operations.</summary>
internal static class MethodGeneratorEmitter
{
    /// <summary>TryCreate,TryCreate with error message methods for the specified type, and ValidateOrThrow.</summary>
    /// <param name="builder">The source code builder.</param>
    /// <param name="data">The generator data containing type information.</param>
    internal static void GenerateMandatoryMethods(this SourceCodeBuilder builder, GeneratorData data)
    {
        builder
            .AppendSummary("Tries to create an instance of AsciiString from the specified value.")
            .AppendParamDescription("value", $"The value to create {data.ClassName} from")
            .AppendParamDescription(
                "result",
                $"When this method returns, contains the created {data.ClassName} if the conversion succeeded, or null if the conversion failed."
            )
            .AppendReturnsDescription("true if the conversion succeeded; otherwise, false.");

        var primitiveType =
            data.ParentSymbols.Count != 0 ? data.ParentSymbols[0].GetFriendlyName() : data.PrimitiveTypeFriendlyName;

        builder
            .Append("public static bool TryCreate(")
            .Append(primitiveType)
            .Append(" value, [NotNullWhen(true)] out ")
            .Append(data.ClassName)
            .AppendLine("? result)")
            .OpenBracket()
            .Append("return TryCreate(value, out result, out _);")
            .CloseBracket()
            .NewLine();

        builder
            .AppendSummary("Tries to create an instance of AsciiString from the specified value.")
            .AppendParamDescription("value", $"The value to create {data.ClassName} from")
            .AppendParamDescription(
                "result",
                $"When this method returns, contains the created {data.ClassName} if the conversion succeeded, or null if the conversion failed."
            )
            .AppendParamDescription(
                "errorMessage",
                "When this method returns, contains the error message if the conversion failed; otherwise, null."
            )
            .AppendReturnsDescription("true if the conversion succeeded; otherwise, false.");

        builder
            .Append("public static bool TryCreate(")
            .Append(primitiveType)
            .Append(" value,[NotNullWhen(true)]  out ")
            .Append(data.ClassName)
            .AppendLine("? result, [NotNullWhen(false)]  out string? errorMessage)")
            .OpenBracket();

        if (data.StringLengthAttributeValidation is not null)
        {
            var (minValue, maxValue) = data.StringLengthAttributeValidation.Value;
            var hasMinValue = minValue >= 0;
            var hasMaxValue = maxValue != int.MaxValue;

            if (hasMinValue || hasMaxValue)
            {
                var minValueText = minValue.ToString(CultureInfo.InvariantCulture);
                var maxValueText = maxValue.ToString(CultureInfo.InvariantCulture);

                builder
                    .Append("if (value.Length is ")
                    .AppendIf(hasMinValue, $"< {minValueText}")
                    .AppendIf(hasMinValue && hasMaxValue, " or ")
                    .AppendIf(hasMaxValue, $"> {maxValueText}")
                    .AppendLine(")")
                    .OpenBracket()
                    .AppendLine("result = null;")
                    .AppendLine($"errorMessage =\" String length is out of range {minValueText}..{maxValueText}\";")
                    .AppendLine("return false;")
                    .CloseBracket();
            }
        }

        builder
            .AppendLine("var validationResult = Validate(value);")
            .NewLine()
            .AppendLine("if (!validationResult.IsValid)")
            .OpenBracket()
            .AppendLine("result = null;")
            .AppendLine("errorMessage = validationResult.ErrorMessage;")
            .AppendLine("return false;")
            .CloseBracket()
            .NewLine()
            .AppendLine("result = new (value, false);")
            .AppendLine("errorMessage = null;")
            .AppendLine("return true;")
            .CloseBracket()
            .NewLine();

        builder
            .AppendSummary("Validates the specified value and throws an exception if it is not valid.")
            .AppendParamDescription("value", "The value to validate")
            .AppendExceptionDescription("InvalidPrimitiveValueException", "Thrown when the value is not valid.");

        builder
            .AppendLine($"public void ValidateOrThrow({primitiveType} value)")
            .OpenBracket()
            .AppendLine("var result = Validate(value);")
            .NewLine()
            .AppendLine("if (!result.IsValid)")
            .OpenBracket()
            .AppendLine("throw new InvalidPrimitiveValueException(result.ErrorMessage, this);")
            .CloseBracket()
            .CloseBracket();
    }

    /// <summary>Generates implicit operators for a specified class.</summary>
    /// <param name="builder">The SourceCodeBuilder for generating source code.</param>
    /// <param name="data">The GeneratorData for the class.</param>
    internal static void GenerateImplicitOperators(this SourceCodeBuilder builder, GeneratorData data)
    {
        var primitiveName = data.ClassName;
        var primitiveType = data.TypeSymbol;
        var underlyingFriendlyName = data.PrimitiveTypeFriendlyName;
        var underlyingType = data.PrimitiveTypeSymbol;

        // From Underlying to our type
        if (underlyingType.IsValueType || primitiveType.IsValueType)
        {
            builder
                .AppendSummary(
                    $"Implicit conversion from <see cref = \"{underlyingFriendlyName}\"/> to <see cref = \"{primitiveName}\"/>"
                )
                .AppendMethodAggressiveInliningAttribute()
                .Append($"public static implicit operator {primitiveName}({underlyingFriendlyName} value)")
                .AppendLine(" => new(value);")
                .NewLine();
        }

        builder
            .AppendSummary(
                $"Implicit conversion from <see cref = \"{underlyingFriendlyName}\"/> (nullable) to <see cref = \"{primitiveName}\"/> (nullable)"
            )
            .AppendMethodAggressiveInliningAttribute()
            .AppendLine("[return: NotNullIfNotNull(nameof(value))]")
            .Append($"public static implicit operator {primitiveName}?({underlyingFriendlyName}? value)")
            .AppendLine($" => value is null ? null : new(value{(underlyingType.IsValueType ? ".Value" : "")});")
            .NewLine();

        // From our type to underlying type
        if (underlyingType.IsValueType || primitiveType.IsValueType)
        {
            builder
                .AppendSummary(
                    $"Implicit conversion from <see cref = \"{primitiveName}\"/> to <see cref = \"{underlyingFriendlyName}\"/>"
                )
                .AppendMethodAggressiveInliningAttribute()
                .Append($"public static implicit operator {underlyingFriendlyName}({primitiveName} value)")
                .AppendLine($" => ({underlyingFriendlyName})value.{data.FieldName};")
                .NewLine();
        }

        builder
            .AppendSummary(
                $"Implicit conversion from <see cref = \"{primitiveName}\"/> (nullable) to <see cref = \"{underlyingFriendlyName}\"/> (nullable)"
            )
            .AppendMethodAggressiveInliningAttribute()
            .AppendLine("[return: NotNullIfNotNull(nameof(value))]")
            .Append($"public static implicit operator {underlyingFriendlyName}?({primitiveName}? value)")
            .AppendLine(
                $" => value is null ? null : ({underlyingFriendlyName}?)value{(primitiveType.IsValueType ? ".Value" : "")}.{data.FieldName};"
            )
            .NewLine();

        if (data.ParentSymbols.Count != 0)
        {
            var parentClassName = data.ParentSymbols[0].Name;

            if (primitiveType.IsValueType)
            {
                builder
                    .AppendSummary(
                        $"Implicit conversion from <see cref = \"{parentClassName}\"/> to <see cref = \"{primitiveName}\"/>"
                    )
                    .AppendMethodAggressiveInliningAttribute()
                    .Append($"public static implicit operator {primitiveName}({parentClassName} value)")
                    .AppendLine(" => new(value);")
                    .NewLine();
            }

            builder
                .AppendSummary(
                    $"Implicit conversion from <see cref = \"{parentClassName}\"/> (nullable) to <see cref = \"{primitiveName}\"/> (nullable)"
                )
                .AppendMethodAggressiveInliningAttribute()
                .AppendLine("[return: NotNullIfNotNull(nameof(value))]")
                .Append($"public static implicit operator {primitiveName}?({parentClassName}? value)")
                .AppendLine(
                    $" => value is null ? null : ({primitiveName}?)value{(underlyingType.IsValueType ? ".Value" : "")};"
                )
                .NewLine();
        }

        if (data.UnderlyingType is PrimitiveUnderlyingType.DateOnly or PrimitiveUnderlyingType.TimeOnly)
        {
            builder
                .AppendSummary(
                    $"Implicit conversion from <see cref = \"{primitiveName}\"/> to <see cref = \"DateTime\"/>"
                )
                .AppendMethodAggressiveInliningAttribute()
                .Append($"public static implicit operator DateTime({primitiveName} value)")
                .AppendLine($" => (({underlyingFriendlyName})value.{data.FieldName}).ToDateTime();")
                .NewLine();

            builder
                .AppendSummary(
                    $"Implicit conversion from <see cref = \"DateTime\"/> to <see cref = \"{primitiveName}\"/>"
                )
                .AppendMethodAggressiveInliningAttribute()
                .Append($"public static implicit operator {primitiveName}(DateTime value)")
                .AppendLine($" => {data.UnderlyingType}.FromDateTime(value);")
                .NewLine();
        }
    }

    /// <summary>Generates comparison operators (&lt;, &lt;=, &gt;, &gt;=) for the specified type.</summary>
    /// <param name="builder">The source code builder.</param>
    /// <param name="className">The name of the class.</param>
    /// <param name="fieldName">The name of the field to compare.</param>
    internal static void GenerateComparisonCode(this SourceCodeBuilder builder, string className, string fieldName)
    {
        builder
            .AppendInheritDoc()
            .AppendMethodAggressiveInliningAttribute()
            .Append($"public static bool operator <({className} left, {className} right)")
            .AppendLine($" => left.{fieldName} < right.{fieldName};")
            .NewLine();

        builder
            .AppendInheritDoc()
            .AppendMethodAggressiveInliningAttribute()
            .Append($"public static bool operator <=({className} left, {className} right)")
            .AppendLine($" => left.{fieldName} <= right.{fieldName};")
            .NewLine();

        builder
            .AppendInheritDoc()
            .AppendMethodAggressiveInliningAttribute()
            .Append($"public static bool operator >({className} left, {className} right)")
            .AppendLine($" => left.{fieldName} > right.{fieldName};")
            .NewLine();

        builder
            .AppendInheritDoc()
            .AppendMethodAggressiveInliningAttribute()
            .Append($"public static bool operator >=({className} left, {className} right)")
            .AppendLine($" => left.{fieldName} >= right.{fieldName};");
    }

    /// <summary>Generates an addition operator for the specified type.</summary>
    /// <param name="builder">The source code builder.</param>
    /// <param name="className">The name of the class.</param>
    /// <param name="fieldName">The name of the field to perform addition on.</param>
    internal static void GenerateAdditionCode(this SourceCodeBuilder builder, string className, string fieldName)
    {
        builder
            .AppendInheritDoc()
            .AppendMethodAggressiveInliningAttribute()
            .Append($"public static {className} operator +({className} left, {className} right)")
            .AppendLine($" => new(left.{fieldName} + right.{fieldName});");
    }

    /// <summary>Generates a subtraction operator for the specified type.</summary>
    /// <param name="builder">The source code builder.</param>
    /// <param name="className">The name of the class.</param>
    /// <param name="fieldName">The name of the field to perform subtraction on.</param>
    public static void GenerateSubtractionCode(this SourceCodeBuilder builder, string className, string fieldName)
    {
        builder
            .AppendInheritDoc()
            .AppendMethodAggressiveInliningAttribute()
            .Append($"public static {className} operator -({className} left, {className} right)")
            .AppendLine($" => new(left.{fieldName} - right.{fieldName});");
    }

    /// <summary>Generates a division operator for the specified type.</summary>
    /// <param name="builder">The source code builder.</param>
    /// <param name="className">The name of the class.</param>
    /// <param name="fieldName">The name of the field to perform division on.</param>
    public static void GenerateDivisionCode(this SourceCodeBuilder builder, string className, string fieldName)
    {
        builder
            .AppendInheritDoc()
            .AppendMethodAggressiveInliningAttribute()
            .Append($"public static {className} operator /({className} left, {className} right)")
            .AppendLine($" => new(left.{fieldName} / right.{fieldName});");
    }

    /// <summary>Generates a multiplication operator for the specified type.</summary>
    /// <param name="builder">The source code builder.</param>
    /// <param name="className">The name of the class.</param>
    /// <param name="fieldName">The name of the field to perform multiplication on.</param>
    public static void GenerateMultiplyCode(this SourceCodeBuilder builder, string className, string fieldName)
    {
        builder
            .AppendInheritDoc()
            .AppendMethodAggressiveInliningAttribute()
            .Append($"public static {className} operator *({className} left, {className} right)")
            .AppendLine($" => new(left.{fieldName} * right.{fieldName});");
    }

    /// <summary>Generates a modulus operator for the specified type.</summary>
    /// <param name="builder">The source code builder.</param>
    /// <param name="className">The name of the class.</param>
    /// <param name="fieldName">The name of the field to perform modulus on.</param>
    public static void GenerateModulusCode(this SourceCodeBuilder builder, string className, string fieldName)
    {
        builder
            .AppendInheritDoc()
            .AppendMethodAggressiveInliningAttribute()
            .Append($"public static {className} operator %({className} left, {className} right)")
            .AppendLine($" => new(left.{fieldName} % right.{fieldName});");
    }

    /// <summary>Generates string methods</summary>
    /// <param name="builder">The source code builder.</param>
    public static void GenerateStringMethods(this SourceCodeBuilder builder)
    {
        builder
            .AppendSummary("Gets the character at the specified index.")
            .AppendLine("public char this[int i]")
            .OpenBracket()
            .AppendLine("get => _value[i];")
            .CloseBracket()
            .NewLine();
        builder
            .AppendSummary("Gets the character at the specified index.")
            .AppendLine("public char this[global::System.Index index]")
            .OpenBracket()
            .AppendLine("get => _value[index];")
            .CloseBracket()
            .NewLine();
        builder
            .AppendSummary("Gets the substring by specified range.")
            .AppendLine("public string this[global::System.Range range]")
            .OpenBracket()
            .AppendLine("get => _value[range];")
            .CloseBracket()
            .NewLine();
        builder
            .AppendSummary("Gets the number of characters.")
            .AppendSummaryBlock("return", "The number of characters in underlying string value.")
            .AppendLine("public int Length => _value.Length;")
            .NewLine();
        builder
            .AppendSummary("Returns a substring of this string.")
            .AppendMethodAggressiveInliningAttribute()
            .AppendLine("public string Substring(int startIndex, int length) => _value.Substring(startIndex, length);")
            .NewLine();
        builder
            .AppendSummary("Returns a substring of this string.")
            .AppendMethodAggressiveInliningAttribute()
            .AppendLine("public string Substring(int startIndex) => _value.Substring(startIndex);")
            .NewLine();
        builder
            .AppendSummary("Returns the entire string as an array of characters.")
            .AppendMethodAggressiveInliningAttribute()
            .AppendLine("public char[] ToCharArray() => _value.ToCharArray();");
    }

    /// <summary>Generates methods required for implementing ISpanFormattable for the specified type.</summary>
    /// <param name="builder">The source code builder.</param>
    /// <param name="data">The <see cref="GeneratorData"/> object containing information about the data type.</param>
    public static void GenerateSpanFormattable(this SourceCodeBuilder builder, GeneratorData data)
    {
        var syntaxAttribute = data.PrimitiveTypeSymbol.GetStringSyntaxAttribute();

        builder
            .AppendInheritDoc()
            .AppendMethodAggressiveInliningAttribute()
            .AppendLine(
                $"public string ToString({syntaxAttribute}string? format, {TypeNames.IFormatProvider}? formatProvider) => {data.FieldName}.ToString(format, formatProvider);"
            )
            .NewLine();

        builder
            .AppendInheritDoc()
            .AppendMethodAggressiveInliningAttribute()
            .AppendLine(
                $"public bool TryFormat({TypeNames.Span}<char> destination, out int charsWritten, {syntaxAttribute}{TypeNames.ReadOnlySpan}<char> format, {TypeNames.IFormatProvider}? provider)"
            )
            .OpenBracket()
            .Append($"return (({TypeNames.ISpanFormattable})")
            .Append(data.FieldName)
            .AppendLine(").TryFormat(destination, out charsWritten, format, provider);")
            .CloseBracket();
    }

    /// <summary>Generates a method for formatting to UTF-8 if the condition NET8_0_OR_GREATER is met.</summary>
    /// <param name="builder">The <see cref="SourceCodeBuilder"/> used to build the source code.</param>
    /// <param name="data">The <see cref="GeneratorData"/> object containing information about the data type.</param>
    internal static void GenerateUtf8Formattable(this SourceCodeBuilder builder, GeneratorData data)
    {
        var syntaxAttribute = data.PrimitiveTypeSymbol.GetStringSyntaxAttribute();

        builder
            .AppendPreProcessorDirective("if NET8_0_OR_GREATER")
            .AppendInheritDoc($"{TypeNames.IUtf8SpanFormattable}.TryFormat")
            .AppendMethodAggressiveInliningAttribute()
            .AppendLine(
                $"public bool TryFormat({TypeNames.Span}<byte> utf8Destination, out int bytesWritten, {syntaxAttribute}{TypeNames.ReadOnlySpan}<char> format, {TypeNames.IFormatProvider}? provider)"
            )
            .OpenBracket()
            .Append($"return (({TypeNames.IUtf8SpanFormattable})")
            .Append(data.FieldName)
            .AppendLine(").TryFormat(utf8Destination, out bytesWritten, format, provider);")
            .CloseBracket()
            .AppendPreProcessorDirective("endif");
    }

    /// <summary>Generates Parse and TryParse methods for the specified type.</summary>
    /// <param name="builder">The <see cref="SourceCodeBuilder"/> used to build the source code.</param>
    /// <param name="data">The <see cref="GeneratorData"/> object containing information about the data type.</param>
    /// <remarks>This method generates parsing methods based on the provided data type and serialization format.</remarks>
    public static void GenerateParsable(this SourceCodeBuilder builder, GeneratorData data)
    {
        var underlyingType =
            data.ParentSymbols.Count == 0 ? data.PrimitiveTypeFriendlyName : data.ParentSymbols[0].Name;

        var dataClassName = data.ClassName;
        var format = data.SerializationFormat;
        var isString = data.IsPrimitiveUnderlyingTypString();
        var isChar = data.IsPrimitiveUnderlyingTypeChar();
        var isBool = data.IsPrimitiveUnderlyingTypeBool();

        #region T Parse(ReadOnlySpan<char> s, IFormatProvider? provider)

        builder
            .AppendInheritDoc()
            .AppendMethodAggressiveInliningAttribute()
            .Append(
                $"public static {dataClassName} Parse({TypeNames.ReadOnlySpan}<char> s, {TypeNames.IFormatProvider}? provider) => "
            );

        if (isString)
        {
            builder.AppendLine("s.ToString();");
        }
        else if (isChar)
        {
            builder.AppendLine("char.Parse(s);");
        }
        else if (isBool)
        {
            builder.AppendLine("bool.Parse(s);");
        }
        else
        {
            builder
                .Append($"{underlyingType}.")
                .AppendLineIfElse(format is null, "Parse(s, provider);", $"ParseExact(s, \"{format}\", provider);");
        }

        #endregion

        builder.NewLine();

        #region T Parse(string s, IFormatProvider? provider)

        builder
            .AppendInheritDoc()
            .AppendMethodAggressiveInliningAttribute()
            .AppendLine(
                $"public static {dataClassName} Parse(string s, {TypeNames.IFormatProvider}? provider) => Parse(s.AsSpan(), provider);"
            );

        #endregion

        builder.NewLine();

        #region bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out T result)

        builder
            .AppendInheritDoc()
            .AppendLine(
                $"public static bool TryParse({TypeNames.ReadOnlySpan}<char> s, {TypeNames.IFormatProvider}? provider, [MaybeNullWhen(false)] out {dataClassName} result)"
            )
            .OpenBracket();

        if (isString)
        {
            builder.AppendLine("var value = s.ToString();");
            builder.AppendLine("if (s.IsEmpty)");
        }
        else if (isChar)
        {
            builder.AppendLine("if (!char.TryParse(s, out var value))");
        }
        else if (isBool)
        {
            builder.AppendLine("if (!bool.TryParse(s, out var value))");
        }
        else
        {
            builder
                .AppendIf(format is null, $"if (!{underlyingType}.TryParse(s, provider, out var value))")
                .AppendIf(format is not null, $"if (!{underlyingType}.TryParseExact(s, \"{format}\", out var value))");
        }

        builder.OpenBracket().AppendLine("result = default;").AppendLine("return false;").CloseBracket().NewLine();

        if (!data.TypeSymbol.IsValueType)
        {
            builder.AppendLine($"return {dataClassName}.TryCreate(value, out result);");
        }
        else
        {
            builder
                .AppendLine("if (TryCreate(value, out var created))")
                .OpenBracket()
                .AppendLine("result = created.Value;")
                .AppendLine("return true;")
                .CloseBracket()
                .NewLine()
                .AppendLine("result = default;")
                .AppendLine("return false;");
        }

        builder.CloseBracket();

        #endregion

        builder.NewLine();

        #region bool TryParse(string? s, IFormatProvider? provider, out T result)

        builder
            .AppendInheritDoc()
            .AppendLine(
                $"public static bool TryParse([NotNullWhen(true)] string? s, {TypeNames.IFormatProvider}? provider, [MaybeNullWhen(false)] out {dataClassName} result) => TryParse(s is null ? [] : s.AsSpan(), provider, out result);"
            );

        #endregion
    }

    /// <summary>Generates code for implementing the IComparable interface for the specified type.</summary>
    /// <param name="builder">The source code builder.</param>
    /// <param name="className">The name of the class.</param>
    /// <param name="isValueType">A flag indicating if the type is a value type.</param>
    internal static void GenerateComparableCode(this SourceCodeBuilder builder, string className, bool isValueType)
    {
        builder
            .AppendInheritDoc()
            .AppendLine("public int CompareTo(object? obj)")
            .OpenBracket()
            .AppendLine("return obj switch")
            .OpenBracket()
            .AppendLine("null => 1,")
            .AppendLine($"{className} c => CompareTo(c),")
            .AppendLine($"_ => throw new ArgumentException(\"Object is not a {className}\", nameof(obj)),")
            .CloseExpressionBracket()
            .CloseBracket();

        var nullable = isValueType ? "" : "?";

        builder
            .NewLine()
            .AppendInheritDoc()
            .AppendLine($"public int CompareTo({className}{nullable} other)")
            .OpenBracket()
            .Append("if (")
            .AppendIf(!isValueType, "other is null || ")
            .AppendLine("!other._isInitialized)")
            .OpenBracket()
            .AppendLine("return 1;")
            .CloseBracket()
            .NewLine()
            .AppendLine("if (!_isInitialized)")
            .OpenBracket()
            .AppendLine("return -1;")
            .CloseBracket()
            .NewLine()
            .AppendLine("return _value.CompareTo(other._value);")
            .CloseBracket();
    }

    /// <summary>Generates equality and inequality operators for the specified type.</summary>
    /// <param name="builder">The source code builder.</param>
    /// <param name="className">The name of the class.</param>
    /// <param name="isValueType">A flag indicating if the type is a value type.</param>
    public static void GenerateEquatableOperators(this SourceCodeBuilder builder, string className, bool isValueType)
    {
        builder
            .AppendInheritDoc()
            .AppendMethodAggressiveInliningAttribute()
            .AppendLine($"public override bool Equals(object? obj) => obj is {className} other && Equals(other);")
            .NewLine();

        var nullable = isValueType ? "" : "?";

        builder
            .AppendInheritDoc()
            .AppendMethodAggressiveInliningAttribute()
            .AppendLine($"public bool Equals({className}{nullable} other)")
            .OpenBracket()
            .Append("if (")
            .AppendIf(!isValueType, "other is null || ")
            .AppendLine("!_isInitialized || !other._isInitialized)")
            .OpenBracket()
            .AppendLine("return false;")
            .CloseBracket()
            .NewLine()
            .AppendLine("return _value.Equals(other._value);")
            .CloseBracket()
            .NewLine();

        builder
            .AppendMethodAggressiveInliningAttribute()
            .Append($"public static bool operator ==({className}{nullable} left, {className}{nullable} right)");

        if (isValueType)
        {
            builder.AppendLine(" => left.Equals(right);");
        }
        else
        {
            builder.NewLine();

            builder
                .OpenBracket()
                .AppendLine("if (ReferenceEquals(left, right))")
                .AppendIndentation()
                .AppendLine("return true;")
                .AppendLine("if (left is null || right is null)")
                .AppendIndentation()
                .AppendLine("return false;")
                .AppendLine("return left.Equals(right);")
                .CloseBracket()
                .NewLine();
        }

        builder.NewLine();

        builder
            .AppendMethodAggressiveInliningAttribute()
            .AppendLine(
                $"public static bool operator !=({className}{nullable} left, {className}{nullable} right) => !(left == right);"
            );
    }

    /// <summary>Generates the necessary methods for implementing the IXmlSerializable interface.</summary>
    /// <param name="builder">The source code builder.</param>
    /// <param name="data">The generator data.</param>
    public static void GenerateIXmlSerializableMethods(this SourceCodeBuilder builder, GeneratorData data)
    {
        builder.AppendInheritDoc();
        builder.AppendLine("public XmlSchema? GetSchema() => null;").NewLine();

        var method = data.PrimitiveTypeFriendlyName switch
        {
            "string" => "ReadElementContentAsString",
            "bool" => "ReadElementContentAsBoolean",
            _ => $"ReadElementContentAs<{data.PrimitiveTypeFriendlyName}>",
        };

        builder.AppendInheritDoc();

        builder
            .AppendLine("public void ReadXml(XmlReader reader)")
            .OpenBracket()
            .Append("var value = reader.")
            .Append(method)
            .AppendLine("();")
            .AppendLine("ValidateOrThrow(value);")
            .AppendLine("System.Runtime.CompilerServices.Unsafe.AsRef(in _value) = value;")
            .AppendLine("System.Runtime.CompilerServices.Unsafe.AsRef(in _isInitialized) = true;")
            .CloseBracket()
            .NewLine();

        builder.AppendInheritDoc();

        if (string.Equals(data.PrimitiveTypeFriendlyName, "string", StringComparison.Ordinal))
        {
            builder.AppendLine($"public void WriteXml(XmlWriter writer) => writer.WriteString({data.FieldName});");
        }
        else if (data.SerializationFormat is null)
        {
            builder.AppendLine(
                $"public void WriteXml(XmlWriter writer) => writer.WriteValue((({data.PrimitiveTypeFriendlyName}){data.FieldName}).ToXmlString());"
            );
        }
        else
        {
            builder.AppendLine(
                $"public void WriteXml(XmlWriter writer) => writer.WriteString({data.FieldName}.ToString(\"{data.SerializationFormat}\"));"
            );
        }
    }

    /// <summary>Generates IConvertible interface methods for the specified type.</summary>
    /// <param name="builder">The source code builder.</param>
    /// <param name="data">The generator data containing type information.</param>
    internal static void GenerateConvertibles(this SourceCodeBuilder builder, GeneratorData data)
    {
        var fieldName = $"({data.UnderlyingType}){data.FieldName}";

        if (data.UnderlyingType is PrimitiveUnderlyingType.DateOnly or PrimitiveUnderlyingType.TimeOnly)
        {
            fieldName = '(' + fieldName + ").ToDateTime()";
        }

        builder.AppendInheritDoc();
        builder.AppendMethodAggressiveInliningAttribute();

        builder
            .Append($"{TypeNames.TypeCode} {TypeNames.IConvertible}.GetTypeCode()")
            .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).GetTypeCode();")
            .NewLine();

        builder.AppendInheritDoc();

        builder
            .Append($"bool {TypeNames.IConvertible}.ToBoolean({TypeNames.IFormatProvider}? provider)")
            .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).ToBoolean(provider);")
            .NewLine();

        builder.AppendInheritDoc();

        builder
            .Append($"byte {TypeNames.IConvertible}.ToByte({TypeNames.IFormatProvider}? provider)")
            .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).ToByte(provider);")
            .NewLine();

        builder.AppendInheritDoc();

        builder
            .Append($"char {TypeNames.IConvertible}.ToChar({TypeNames.IFormatProvider}? provider)")
            .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).ToChar(provider);")
            .NewLine();

        builder.AppendInheritDoc();

        builder
            .Append($"{TypeNames.DateTime} {TypeNames.IConvertible}.ToDateTime({TypeNames.IFormatProvider}? provider)")
            .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).ToDateTime(provider);")
            .NewLine();

        builder.AppendInheritDoc();

        builder
            .Append($"decimal {TypeNames.IConvertible}.ToDecimal({TypeNames.IFormatProvider}? provider)")
            .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).ToDecimal(provider);")
            .NewLine();

        builder.AppendInheritDoc();

        builder
            .Append($"double {TypeNames.IConvertible}.ToDouble({TypeNames.IFormatProvider}? provider)")
            .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).ToDouble(provider);")
            .NewLine();

        builder.AppendInheritDoc();

        builder
            .Append($"short {TypeNames.IConvertible}.ToInt16({TypeNames.IFormatProvider}? provider)")
            .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).ToInt16(provider);")
            .NewLine();

        builder.AppendInheritDoc();

        builder
            .Append($"int {TypeNames.IConvertible}.ToInt32({TypeNames.IFormatProvider}? provider)")
            .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).ToInt32(provider);")
            .NewLine();

        builder.AppendInheritDoc();

        builder
            .Append($"long {TypeNames.IConvertible}.ToInt64({TypeNames.IFormatProvider}? provider)")
            .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).ToInt64(provider);")
            .NewLine();

        builder.AppendInheritDoc();

        builder
            .Append($"sbyte {TypeNames.IConvertible}.ToSByte({TypeNames.IFormatProvider}? provider)")
            .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).ToSByte(provider);")
            .NewLine();

        builder.AppendInheritDoc();

        builder
            .Append($"float {TypeNames.IConvertible}.ToSingle({TypeNames.IFormatProvider}? provider)")
            .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).ToSingle(provider);")
            .NewLine();

        builder.AppendInheritDoc();

        builder
            .Append($"string {TypeNames.IConvertible}.ToString({TypeNames.IFormatProvider}? provider)")
            .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).ToString(provider);")
            .NewLine();

        builder.AppendInheritDoc();

        builder
            .Append(
                $"object {TypeNames.IConvertible}.ToType(Type conversionType, {TypeNames.IFormatProvider}? provider)"
            )
            .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).ToType(conversionType, provider);")
            .NewLine();

        builder.AppendInheritDoc();

        builder
            .Append($"ushort {TypeNames.IConvertible}.ToUInt16({TypeNames.IFormatProvider}? provider)")
            .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).ToUInt16(provider);")
            .NewLine();

        builder.AppendInheritDoc();

        builder
            .Append($"uint {TypeNames.IConvertible}.ToUInt32({TypeNames.IFormatProvider}? provider)")
            .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).ToUInt32(provider);")
            .NewLine();

        builder.AppendInheritDoc();

        builder
            .Append($"ulong {TypeNames.IConvertible}.ToUInt64({TypeNames.IFormatProvider}? provider)")
            .AppendLine($" => (({TypeNames.IConvertible}){fieldName}).ToUInt64(provider);");
    }
}
