// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Generator.Primitives;
using Framework.Generator.Primitives.Models;
using Microsoft.CodeAnalysis;
using Tests.Helpers;

namespace Tests;

public sealed class PrimitiveGeneratorTests
{
    private const int _GenerateAllFileCounts = 8;

    private readonly PrimitiveGlobalOptions _generateAllOptions =
        new()
        {
            GenerateJsonConverters = true, // generate 1 files
            GenerateTypeConverters = true, // generate 1 files
            GenerateSwashbuckleSwaggerConverters = true, // generate 1 files
            GenerateNswagSwaggerConverters = true, // generate 1 files
            GenerateXmlConverters = true, // generate methods for XML serialization
            GenerateEntityFrameworkValueConverters = true, // generate 2 files
            GenerateDapperConverters = true, // generate 2 files
        };

    [Fact]
    public Task should_generate_all_converters_when_string_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            public partial class StringPrimitive : IPrimitive<string>
            {
                public static PrimitiveValidationResult Validate(string value)
                {
                    return string.Equals(value, "Test", StringComparison.Ordinal) ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_guid_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            public readonly partial struct GuidPrimitive : IPrimitive<Guid>
            {
                public static PrimitiveValidationResult Validate(Guid value)
                {
                    return value == default ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_bool_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            public readonly partial struct BoolPrimitive : IPrimitive<bool>
            {
                public static PrimitiveValidationResult Validate(bool value)
                {
                   return !value ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_sbyte_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            public readonly partial struct SBytePrimitive : IPrimitive<SByte>
            {
                public static PrimitiveValidationResult Validate(SByte value)
                {
                    return value < 10 || value > 20 ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_byte_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            public readonly partial struct BytePrimitive : IPrimitive<byte>
            {
                public static PrimitiveValidationResult Validate(byte value)
                {
                    return value < 10 || value > 20 ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_short_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            /// <inheritdoc/>
            public readonly partial struct ShortPrimitive : IPrimitive<short>
            {
                /// <inheritdoc/>
                public static PrimitiveValidationResult Validate(short value)
                {
                   return value < 0 ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_ushort_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            /// <inheritdoc/>
            public readonly partial struct UShortPrimitive : IPrimitive<ushort>
            {
                /// <inheritdoc/>
                public static PrimitiveValidationResult Validate(ushort value)
                {
                    return value > 100 ? "Value must be less than or equal to 100" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_int_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            /// <inheritdoc/>
            public readonly partial struct IntPrimitive : IPrimitive<int>
            {
                /// <inheritdoc/>
                public static PrimitiveValidationResult Validate(int value)
                {
                    return value < 10 || value > 20 ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_uint_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            /// <inheritdoc/>
            public readonly partial struct IntPrimitive : IPrimitive<uint>
            {
                /// <inheritdoc/>
                public static PrimitiveValidationResult Validate(uint value)
                {
                    return value < 10 || value > 20 ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_long_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            /// <inheritdoc/>
            public readonly partial struct LongPrimitive : IPrimitive<long>
            {
                /// <inheritdoc/>
                public static PrimitiveValidationResult Validate(long value)
                {
                   return value < 0 ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_ulong_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            /// <inheritdoc/>
            public readonly partial struct LongValue : IPrimitive<ulong>
            {
                /// <inheritdoc/>
                public static PrimitiveValidationResult Validate(ulong value)
                {
                    return value < 0 ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_decimal_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            public readonly partial struct DecimalPrimitive : IPrimitive<decimal>
            {
                /// <inheritdoc/>
                public static PrimitiveValidationResult Validate(decimal value)
                {
                    return value < 0 ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_float_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            public readonly partial struct FloatPrimitive : IPrimitive<float>
            {
                /// <inheritdoc/>
                public static PrimitiveValidationResult Validate(float value)
                {
                    return value < 0 ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_double_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            public readonly partial struct DoublePrimitive : IPrimitive<double>
            {
                /// <inheritdoc/>
                public static PrimitiveValidationResult Validate(double value)
                {
                    return value < 0 ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_TimeSpan_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            public readonly partial struct TimeSpanPrimitive : IPrimitive<TimeSpan>
            {
                /// <inheritdoc/>
                public static PrimitiveValidationResult Validate(TimeSpan value)
                {
                    return value == default ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_DateOnly_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            public readonly partial struct DateOnlyPrimitive : IPrimitive<DateOnly>
            {
                /// <inheritdoc/>
                public static PrimitiveValidationResult Validate(DateOnly value)
                {
                    return value == default ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_TimeOnly_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            public readonly partial struct TimeOnlyPrimitive : IPrimitive<TimeOnly>
            {
                /// <inheritdoc/>
                public static PrimitiveValidationResult Validate(TimeOnly value)
                {
                   return value == default ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_DateTime_primitive()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Text;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            public readonly partial struct DateTimePrimitive : IPrimitive<DateTime>
            {
                /// <inheritdoc/>
                public static PrimitiveValidationResult Validate(DateTime value)
                {
                    if (value == default)
                        return "Invalid Value";

                    return PrimitiveValidationResult.Ok;
                }
            }

            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_DateTimeOffset_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            public readonly partial struct DateTimeOffsetPrimitive : IPrimitive<DateTimeOffset>
            {
                /// <inheritdoc/>
                public static PrimitiveValidationResult Validate(DateTimeOffset value)
                {
                    return value == default ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_char_primitive()
    {
        const string source = """
            using System;
            using System.Text;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            public readonly partial struct CharPrimitive : IPrimitive<char>
            {
                /// <inheritdoc/>
                public static PrimitiveValidationResult Validate(char value)
                {
                    return value == default ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    [Fact]
    public Task should_generate_all_converters_when_string_of_string_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            /// <inheritdoc/>
            public partial class StringPrimitive : IPrimitive<string>
            {
                /// <inheritdoc/>
                public static PrimitiveValidationResult Validate(string value)
                {
                    return value == "Test" ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }

            /// <inheritdoc/>
            public partial class StringOfStringPrimitive : IPrimitive<StringPrimitive>
            {
                /// <inheritdoc/>
                public static PrimitiveValidationResult Validate(StringValue value)
                {
                    return value == "Test" ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }
            """;

        return TestHelper.Verify(source, generated => generated.Files.Should().HaveCount(11), _generateAllOptions);
    }

    [Fact]
    public Task should_generate_all_converters_when_int_of_int_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives

            /// <inheritdoc/>
            public readonly partial struct IntPrimitive : IPrimitive<int>
            {
                /// <inheritdoc/>
                public static PrimitiveValidationResult Validate(int value)
                {
                    return value < 10 || value > 20 ? "Invalid Value" : PrimitiveValidationResult.Ok;
                }
            }

            /// <inheritdoc/>
            public readonly partial struct IntOfIntPrimitive : IPrimitive<IntPrimitive>
            {
                   /// <inheritdoc/>
                   public static PrimitiveValidationResult Validate(IntValue value)
                   {
                        return value < 10 || value > 20 ? "Invalid Value" : PrimitiveValidationResult.Ok;
                   }
            }
            """;

        return TestHelper.Verify(source, generated => generated.Files.Should().HaveCount(11), _generateAllOptions);
    }

    [Fact]
    public Task should_generate_all_converters_when_nested_namespace_primitive()
    {
        const string source = """
            using System;
            using Framework.Generator.Primitives;

            namespace Framework.Primitives
            {
                namespace Outer
                {
                    namespace Nested
                    {
                        public readonly partial struct IntPrimitive : IPrimitive<int>
                        {
                            public static PrimitiveValidationResult Validate(int value)
                            {
                                return value < 10 || value > 20 ? "Invalid Value" : PrimitiveValidationResult.Ok;
                            }
                        }
                    }
                }
            }
            """;

        return TestHelper.Verify(
            source,
            generated => generated.Files.Should().HaveCount(_GenerateAllFileCounts),
            _generateAllOptions
        );
    }

    private static class TestHelper
    {
        internal static Task Verify(
            string source,
            Action<GeneratedOutput>? additionalChecks = null,
            PrimitiveGlobalOptions? options = null
        )
        {
            var generatedOutput = TestHelpers.GetGeneratedOutput<PrimitiveGenerator>(source, options);

            generatedOutput.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
            additionalChecks?.Invoke(generatedOutput);

            return Verifier.Verify(generatedOutput.Driver).UseDirectory("Snapshots");
        }
    }
}
