// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Reflection;
using System.Runtime.Loader;
using Headless.Generator.Primitives;
using Headless.Generator.Primitives.Models;
using Headless.Testing.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

    // Every assembly the test host can load: the whole shared framework plus Dapper and EF Core, which is what a
    // consumer app that turns these converters on compiles against.
    private static readonly Lazy<ImmutableArray<MetadataReference>> _References = new(() =>
        [
            .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)),
        ]
    );
}
