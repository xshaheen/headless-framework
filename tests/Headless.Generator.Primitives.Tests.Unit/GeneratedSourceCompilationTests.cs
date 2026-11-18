// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Data;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Headless.Generator.Primitives;
using Headless.Generator.Primitives.Models;
using Headless.Testing.Tests;
using MessagePack;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NSubstitute;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// Compiles what the generator emits, with every converter turned on, so a syntax or binding error in a generated file
/// fails here instead of in a consumer's build. Snapshot tests compare text only and cannot catch that.
/// </summary>
public sealed class GeneratedSourceCompilationTests : TestBase
{
    private static readonly PrimitiveGlobalOptions _AllConverters = new()
    {
        GenerateJsonConverters = true,
        GenerateTypeConverters = true,
        GenerateXmlConverters = true,
        GenerateEntityFrameworkValueConverters = true,
        GenerateDapperConverters = true,
        GenerateMessagePackFormatters = true,
    };

    public static TheoryData<string> UnderlyingTypes =>
        new(
            "string",
            "Guid",
            "bool",
            "byte",
            "sbyte",
            "short",
            "ushort",
            "int",
            "uint",
            "long",
            "ulong",
            "decimal",
            "float",
            "double",
            "char",
            "DateTime",
            "DateTimeOffset",
            "DateOnly",
            "TimeOnly",
            "TimeSpan"
        );

    [Theory]
    [MemberData(nameof(UnderlyingTypes))]
    public void should_compile_every_generated_file_for_the_underlying_type(string underlyingType)
    {
        // given
        var source = _PrimitiveSource("Billing", "Amount", underlyingType);

        // when
        var (diagnostics, _) = _Compile("Billing.Domain", source);

        // then
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(UnderlyingTypes))]
    public void should_compile_every_generated_file_for_a_primitive_wrapping_another_primitive(string underlyingType)
    {
        // given
        var source = _NestedPrimitiveSource(underlyingType);

        // when
        var (diagnostics, _) = _Compile("Billing.Domain", source);

        // then
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact]
    public void should_compile_a_nested_primitive_with_math_operators_or_a_length_bound()
    {
        // given
        const string source = """
            using Headless.Generator.Primitives;

            namespace Billing
            {
                [SupportedOperations(Addition = true, Subtraction = true, Multiplication = true, Division = true, Modulus = true)]
                public readonly partial struct Count : IPrimitive<int>
                {
                    public static PrimitiveValidationResult Validate(int value) => PrimitiveValidationResult.Ok;
                }

                [SupportedOperations(Addition = true, Subtraction = true, Multiplication = true, Division = true, Modulus = true)]
                public readonly partial struct Quantity : IPrimitive<Count>
                {
                    public static PrimitiveValidationResult Validate(Count value) => PrimitiveValidationResult.Ok;
                }

                public sealed partial class Code : IPrimitive<string>
                {
                    public static PrimitiveValidationResult Validate(string value) => PrimitiveValidationResult.Ok;
                }

                [StringLength(2, 8)]
                public sealed partial class Sku : IPrimitive<Code>
                {
                    public static PrimitiveValidationResult Validate(Code value) => PrimitiveValidationResult.Ok;
                }
            }
            """;

        // when
        var (diagnostics, _) = _Compile("Billing.Domain", source);

        // then
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact]
    public void should_compile_when_two_primitives_share_a_name_in_different_namespaces()
    {
        // given
        var source =
            _PrimitiveSource("Billing", "Id", "int", fileScoped: false)
            + Environment.NewLine
            + _PrimitiveSource("Shipping", "Id", "long", fileScoped: false);

        // when
        var (diagnostics, compilation) = _Compile("Shop", source);

        // then
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        compilation.GetTypeByMetadataName("Billing.Converters.IdDapperTypeHandler").Should().NotBeNull();
        compilation.GetTypeByMetadataName("Shipping.Converters.IdDapperTypeHandler").Should().NotBeNull();
    }

    [Fact]
    public void should_generate_a_partial_primitive_once_when_each_part_has_a_base_list()
    {
        // given
        const string source = """
            using System;
            using Headless.Generator.Primitives;

            namespace Billing;

            public readonly partial struct Amount : IPrimitive<int>
            {
                public static PrimitiveValidationResult Validate(int value) => PrimitiveValidationResult.Ok;
            }

            public readonly partial struct Amount : IEquatable<Amount>;
            """;

        // when
        var (diagnostics, _) = _Compile("Billing.Domain", source);

        // then
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact]
    public void should_compile_a_primitive_whose_namespace_has_a_keyword_segment()
    {
        // given
        var source = _PrimitiveSource("Orders.@event", "Amount", "int");

        // when
        var (diagnostics, compilation) = _Compile("Shop", source);

        // then
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        compilation.GetTypeByMetadataName("Orders.event.Converters.AmountDapperTypeHandler").Should().NotBeNull();
    }

    [Fact]
    public void should_compile_a_primitive_declared_in_the_global_namespace()
    {
        // given
        const string source = """
            using System;
            using Headless.Generator.Primitives;

            public readonly partial struct GlobalId : IPrimitive<int>
            {
                public static PrimitiveValidationResult Validate(int value) => PrimitiveValidationResult.Ok;
            }
            """;

        // when
        var (diagnostics, compilation) = _Compile("Shop", source);

        // then
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        compilation.GetTypeByMetadataName("Converters.GlobalIdDapperTypeHandler").Should().NotBeNull();
    }

    [Theory]
    [InlineData("my-app")]
    [InlineData("2024.Billing")]
    [InlineData("Orders.class")]
    [InlineData("Orders..Api")]
    public void should_compile_the_helpers_when_the_assembly_name_is_not_a_valid_namespace(string assemblyName)
    {
        // given
        var source = _PrimitiveSource("Billing", "Amount", "int");

        // when
        var (diagnostics, _) = _Compile(assemblyName, source);

        // then
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    public static TheoryData<string, object> DapperBoundaryValues =>
        new()
        {
            { "byte", (short)byte.MinValue },
            { "byte", (short)byte.MaxValue },
            { "byte", (int)byte.MaxValue },
            { "byte", (long)byte.MinValue },
            { "short", (int)short.MinValue },
            { "short", (long)short.MaxValue },
            { "short", (decimal)short.MaxValue },
            { "int", (long)int.MaxValue },
            { "int", (long)int.MinValue },
            { "int", (decimal)int.MaxValue },
            { "long", (decimal)long.MaxValue },
            { "long", (decimal)long.MinValue },
            { "float", (double)float.MaxValue },
            { "float", (double)float.MinValue },
            { "long", "-42" },
        };

    [Theory]
    [MemberData(nameof(DapperBoundaryValues))]
    public void should_read_boundary_values_through_the_dapper_type_handler(string underlyingType, object value)
    {
        // given
        var source = _PrimitiveSource("Billing", "Amount", underlyingType);
        var (diagnostics, compilation) = _Compile("Billing.Domain", source);
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        var assembly = _Load(compilation);
        var handlerType = assembly.GetType("Billing.Converters.AmountDapperTypeHandler", throwOnError: true)!;
        var handler = Activator.CreateInstance(handlerType)!;
        var parse = handlerType.GetMethod("Parse", [typeof(object)])!;

        // when
        var primitive = parse.Invoke(handler, [value])!;

        // then
        var primitiveType = assembly.GetType("Billing.Amount", throwOnError: true)!;
        var underlying = primitiveType.GetMethod("GetUnderlyingPrimitiveValue")!.Invoke(primitive, null);
        underlying.Should().Be(Convert.ChangeType(value, underlying!.GetType(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void should_write_the_underlying_value_through_the_dapper_type_handler()
    {
        // given
        const string source = """
            using Headless.Generator.Primitives;

            namespace Billing
            {
                public readonly partial struct Price : IPrimitive<int>
                {
                    public static PrimitiveValidationResult Validate(int value) => PrimitiveValidationResult.Ok;
                }
            }
            """;

        var (diagnostics, compilation) = _Compile("Billing.Domain", source);
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        var assembly = _Load(compilation);
        var priceType = assembly.GetType("Billing.Price", throwOnError: true)!;
        var handlerType = assembly.GetType("Billing.Converters.PriceDapperTypeHandler", throwOnError: true)!;
        var handler = Activator.CreateInstance(handlerType)!;
        var price = handlerType.GetMethod("Parse", [typeof(object)])!.Invoke(handler, [42])!;
        var parameter = Substitute.For<IDbDataParameter>();

        // when
        handlerType.GetMethod("SetValue", [typeof(IDbDataParameter), priceType])!.Invoke(handler, [parameter, price]);

        // then
        parameter.Value.Should().Be(42);
    }

    public static TheoryData<string, string, object, string, string> NestedRoundTrips =>
        new()
        {
            // underlying type, valid text, valid value, text Amount rejects, text Price rejects
            { "decimal", "12.5", 12.5m, "13", "14" },
            { "string", "abcd", "abcd", "bad-amount", "bad-price" },
        };

    [Theory]
    [MemberData(nameof(NestedRoundTrips))]
    public void should_round_trip_a_primitive_wrapping_another_primitive_through_every_converter(
        string underlyingType,
        string text,
        object value,
        string rejectedByAmount,
        string rejectedByPrice
    )
    {
        // given
        var isString = string.Equals(underlyingType, "string", StringComparison.Ordinal);
        var json = isString ? $"\"{text}\"" : text;
        var source = _NestedPrimitiveSource(
            underlyingType,
            isString ? $"\"{rejectedByAmount}\"" : $"{rejectedByAmount}m",
            isString ? $"\"{rejectedByPrice}\"" : $"{rejectedByPrice}m"
        );
        var (diagnostics, compilation) = _Compile("Billing.Domain", source);
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        var assembly = _Load(compilation);
        var amountType = assembly.GetType("Billing.Money.Amount", throwOnError: true)!;
        var priceType = assembly.GetType("Billing.Catalog.Price", throwOnError: true)!;
        var feeType = assembly.GetType("Billing.Catalog.Fee", throwOnError: true)!;
        var price = _Parse(priceType, text);

        // then: IPrimitive<Amount> hands back the wrapped Amount, whose own value is the stored value
        var amount = priceType.GetMethod("GetUnderlyingPrimitiveValue")!.Invoke(price, null)!;
        amount.GetType().Should().Be(amountType);
        amountType.GetMethod("GetUnderlyingPrimitiveValue")!.Invoke(amount, null).Should().Be(value);
        ((IPrimitive)price).GetUnderlyingPrimitiveType().Should().Be(value.GetType());

        // then: parse, format, equality
        // ToString formats the stored value the way the value type does, in the current culture.
        price.ToString().Should().Be(value.ToString());
        price.Should().Be(_Parse(priceType, text));
        price.GetHashCode().Should().Be(_Parse(priceType, text).GetHashCode());

        // then: the Amount to Price conversion
        _ConvertFromParent(priceType, amountType, amount).Should().Be(price);

        // then: JSON reads and writes the innermost value and enforces both primitives' validation
        JsonSerializer.Serialize(price, priceType).Should().Be(json);
        JsonSerializer.Deserialize(json, priceType).Should().Be(price);
        var readRejectedByAmount = () => JsonSerializer.Deserialize(_Json(isString, rejectedByAmount), priceType);
        readRejectedByAmount.Should().Throw<JsonException>();
        var readRejectedByPrice = () => JsonSerializer.Deserialize(_Json(isString, rejectedByPrice), priceType);
        readRejectedByPrice.Should().Throw<JsonException>();

        // then: a primitive two levels deep serializes the same way
        var fee = JsonSerializer.Deserialize(json, feeType)!;
        JsonSerializer.Serialize(fee, feeType).Should().Be(json);
        feeType.GetMethod("GetUnderlyingPrimitiveValue")!.Invoke(fee, null).Should().Be(price);

        // then: TypeConverter
        var typeConverter = (TypeConverter)
            Activator.CreateInstance(assembly.GetType("Billing.Catalog.Converters.PriceTypeConverter", true)!)!;
        typeConverter.ConvertFromInvariantString(text).Should().Be(price);

        // then: EF Core value converter stores the innermost value
        var efConverter = (ValueConverter)
            Activator.CreateInstance(assembly.GetType("Billing.Catalog.Converters.PriceValueConverter", true)!)!;
        efConverter.ProviderClrType.Should().Be(value.GetType());
        efConverter.ConvertToProvider(price).Should().Be(value);
        efConverter.ConvertFromProvider(value).Should().Be(price);

        // then: Dapper
        var handlerType = assembly.GetType("Billing.Catalog.Converters.PriceDapperTypeHandler", throwOnError: true)!;
        var handler = Activator.CreateInstance(handlerType)!;
        handlerType.GetMethod("Parse", [typeof(object)])!.Invoke(handler, [value]).Should().Be(price);
        var parameter = Substitute.For<IDbDataParameter>();
        handlerType.GetMethod("SetValue", [typeof(IDbDataParameter), priceType])!.Invoke(handler, [parameter, price]);
        parameter.Value.Should().Be(value);

        // then: MessagePack
        var packed = MessagePackSerializer.Serialize(priceType, price, cancellationToken: AbortToken);
        MessagePackSerializer.Deserialize(priceType, packed, cancellationToken: AbortToken).Should().Be(price);

        // then: XML
        _XmlRoundTrip(priceType, price).Should().Be(price);
    }

    private static object _Parse(Type primitiveType, string text) =>
        primitiveType
            .GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string), typeof(IFormatProvider)])!
            .Invoke(null, [text, CultureInfo.InvariantCulture])!;

    private static string _Json(bool isString, string text) => isString ? $"\"{text}\"" : text;

    // The nullable form is the one a reference-type primitive gets, and the one that used to call itself.
    private static object? _ConvertFromParent(Type primitiveType, Type parentType, object parent)
    {
        var nullableParent = parentType.IsValueType ? typeof(Nullable<>).MakeGenericType(parentType) : parentType;

        var conversion = primitiveType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m =>
                string.Equals(m.Name, "op_Implicit", StringComparison.Ordinal)
                && m.GetParameters()[0].ParameterType == nullableParent
            );

        return conversion.Invoke(null, [parent]);
    }

    private static object _XmlRoundTrip(Type primitiveType, object primitive)
    {
        var xml = new StringBuilder();

        using (var writer = XmlWriter.Create(xml))
        {
            writer.WriteStartElement("value");
            ((IXmlSerializable)primitive).WriteXml(writer);
            writer.WriteEndElement();
        }

        // The parameterless constructor is obsolete as an error for callers, but reflection still reaches it, which is
        // how XmlSerializer creates the instance it reads into.
        var read = Activator.CreateInstance(primitiveType)!;

        using var reader = XmlReader.Create(new StringReader(xml.ToString()));
        reader.MoveToContent();
        ((IXmlSerializable)read).ReadXml(reader);

        return read;
    }

    private static string _PrimitiveSource(
        string @namespace,
        string name,
        string underlyingType,
        bool fileScoped = true
    )
    {
        var kind = string.Equals(underlyingType, "string", StringComparison.Ordinal)
            ? "sealed partial class"
            : "readonly partial struct";

        var body = $$"""
            public {{kind}} {{name}} : IPrimitive<{{underlyingType}}>
            {
                public static PrimitiveValidationResult Validate({{underlyingType}} value) => PrimitiveValidationResult.Ok;
            }
            """;

        return fileScoped
            ? $$"""
                using System;
                using Headless.Generator.Primitives;

                namespace {{@namespace}};

                {{body}}
                """
            : $$"""
                namespace {{@namespace}}
                {
                    using System;
                    using Headless.Generator.Primitives;

                {{body}}
                }
                """;
    }

    // Price wraps Amount, which wraps the underlying type, and Fee wraps Price. Price lives in another namespace than
    // Amount so the generated files must name Amount's namespace themselves.
    private static string _NestedPrimitiveSource(
        string underlyingType,
        string rejectedByAmount = "default",
        string rejectedByPrice = "default"
    )
    {
        var kind = string.Equals(underlyingType, "string", StringComparison.Ordinal)
            ? "sealed partial class"
            : "readonly partial struct";

        return $$"""
            namespace Billing.Money
            {
                using System;
                using Headless.Generator.Primitives;

                public {{kind}} Amount : IPrimitive<{{underlyingType}}>
                {
                    public static PrimitiveValidationResult Validate({{underlyingType}} value) =>
                        Equals(value, ({{underlyingType}}){{rejectedByAmount}}) ? "Rejected amount" : PrimitiveValidationResult.Ok;
                }
            }

            namespace Billing.Catalog
            {
                using System;
                using Billing.Money;
                using Headless.Generator.Primitives;

                public {{kind}} Price : IPrimitive<Amount>
                {
                    public static PrimitiveValidationResult Validate(Amount value) =>
                        Equals(value.GetUnderlyingPrimitiveValue(), ({{underlyingType}}){{rejectedByPrice}}) ? "Rejected price" : PrimitiveValidationResult.Ok;
                }

                public {{kind}} Fee : IPrimitive<Price>
                {
                    public static PrimitiveValidationResult Validate(Price value) => PrimitiveValidationResult.Ok;
                }
            }
            """;
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, Compilation Compilation) _Compile(
        string assemblyName,
        string source
    )
    {
        var compilation = GeneratorCompilation.Create(assemblyName, [("primitives.cs", source)], _References.Value);

        CSharpGeneratorDriver
            .Create(new PrimitiveGenerator())
            .WithUpdatedParseOptions(GeneratorCompilation.ParseOptions)
            .WithUpdatedAnalyzerConfigOptions(new TestHelpers.PrimitiveConfigOptionsProvider(_AllConverters))
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        return (output.GetDiagnostics(AbortToken).AddRange(generatorDiagnostics), output);
    }

    private static Assembly _Load(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream, cancellationToken: AbortToken);
        result.Success.Should().BeTrue();
        stream.Position = 0;

        // A collectible context per test keeps each emitted copy of the same type names apart.
        return new AssemblyLoadContext(name: null, isCollectible: true).LoadFromStream(stream);
    }

    // Every assembly the test host can load: the whole shared framework plus Dapper, EF Core, and MessagePack, which is
    // what a consumer app that turns these converters on compiles against.
    private static readonly Lazy<ImmutableArray<MetadataReference>> _References = new(() =>
        [
            .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)),
        ]
    );
}
