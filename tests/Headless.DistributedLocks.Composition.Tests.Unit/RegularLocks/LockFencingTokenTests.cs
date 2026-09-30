// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Fencing;
using Headless.Testing.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Tests.RegularLocks;

public sealed class LockFencingTokenTests : TestBase
{
    // Lock tokens and Fencing lease generations come from unrelated sequences; these compilations prove a consumer
    // cannot compare or convert one into the other by accident. The lease-generation side is the real
    // FencedLease.Generation so a later change to that type is caught here too.
    private static readonly Lazy<MetadataReference[]> _References = new(() =>
        [
            .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Where(file =>
                    string.Equals(
                        Path.GetDirectoryName(file),
                        Path.GetDirectoryName(typeof(object).Assembly.Location),
                        StringComparison.Ordinal
                    )
                )
                .Select(file => MetadataReference.CreateFromFile(file)),
            MetadataReference.CreateFromFile(typeof(LockFencingToken).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(FencedLease).Assembly.Location),
        ]
    );

    [Fact]
    public void should_compile_when_tokens_are_compared_with_their_own_kind()
    {
        // The positive control: without it, a missing reference would make every negative case pass for the wrong
        // reason.
        var errors = _Errors(
            """
            var ordered = token > other && token >= other && other < token && other <= token;
            var same = token == other || token != other || token.Equals(other) || token.CompareTo(other) > 0;
            var raw = token.Value;
            var restored = new LockFencingToken(raw);
            var nullable = lease.FencingToken > token;
            """
        );

        errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("_ = token > fenced.Generation;", "CS0019")]
    [InlineData("_ = token < fenced.Generation;", "CS0019")]
    [InlineData("_ = token == fenced.Generation;", "CS0019")]
    [InlineData("_ = lease.FencingToken > fenced.Generation;", "CS0019")]
    [InlineData("_ = token > 5L;", "CS0019")]
    [InlineData("_ = token.CompareTo(fenced.Generation);", "CS1503")]
    [InlineData("long generation = token;", "CS0029")]
    [InlineData("LockFencingToken fromGeneration = fenced.Generation;", "CS0029")]
    [InlineData("LockFencingToken fromLong = 5L;", "CS0029")]
    [InlineData("_ = (long)token;", "CS0030")]
    [InlineData("_ = (LockFencingToken)fenced.Generation;", "CS0030")]
    public void should_not_compile_when_token_is_mixed_with_a_lease_generation_or_long(
        string statement,
        string expectedError
    )
    {
        var errors = _Errors(statement);

        errors.Should().ContainSingle().Which.Id.Should().Be(expectedError);
    }

    [Fact]
    public void should_order_tokens_by_issue_order()
    {
        // given
        var earlier = new LockFencingToken(33);
        var later = new LockFencingToken(34);

        // then
        (earlier < later)
            .Should()
            .BeTrue();
        (later > earlier).Should().BeTrue();
        (earlier <= new LockFencingToken(33)).Should().BeTrue();
        (later >= earlier).Should().BeTrue();
        earlier.CompareTo(later).Should().BeNegative();
        new[] { later, earlier }.Order().Should().Equal(earlier, later);
    }

    [Fact]
    public void should_be_equal_when_values_match()
    {
        // given
        var value = Faker.Random.Long(1);
        var token = new LockFencingToken(value);
        var same = new LockFencingToken(value);

        // then
        (token == same)
            .Should()
            .BeTrue();
        (token != same).Should().BeFalse();
        token.Equals((object)same).Should().BeTrue();
        token.GetHashCode().Should().Be(same.GetHashCode());
        token.Value.Should().Be(value);
    }

    [Fact]
    public void should_not_equal_a_boxed_long_with_the_same_value()
    {
        new LockFencingToken(7).Equals(7L).Should().BeFalse();
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void should_throw_when_value_is_not_positive(long value)
    {
        var act = () => new LockFencingToken(value);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_format_as_invariant_raw_value()
    {
        new LockFencingToken(1_234_567).ToString().Should().Be("1234567");
    }

    private static Diagnostic[] _Errors(string statements)
    {
        var source = $$"""
            using Headless.DistributedLocks;
            using Headless.Fencing;

            public static class Consumer
            {
                public static void Use(IDistributedLease lease, FencedLease fenced, LockFencingToken token, LockFencingToken other)
                {
                    {{statements}}
                }
            }
            """;

        var compilation = CSharpCompilation.Create(
            "LockFencingToken.Consumer",
            [
                CSharpSyntaxTree.ParseText(
                    source,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14)
                ),
            ],
            _References.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        return
        [
            .. compilation
                .GetDiagnostics(AbortToken)
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
        ];
    }
}
