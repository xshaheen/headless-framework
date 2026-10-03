// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.CodeAnalysis;

namespace Tests;

/// <summary>
/// HM013: a <c>RequestAsync&lt;TRequest, TResponse&gt;</c> call is checked against the responders the project can see,
/// in its own assembly or published by a referenced one, and absence of a responder is never reported.
/// </summary>
public sealed class RequestCallCheckTests : TestBase
{
    private const string _Usings = """
        using System.Threading;
        using System.Threading.Tasks;
        using Headless.Messaging;

        """;

    private const string _Contracts = """
        namespace Pricing
        {
            public sealed record GetQuote(string Sku);
            public sealed record Quote(decimal Price);
            public sealed record OtherQuote(decimal Price);
        }

        """;

    private const string _Responder = """
        namespace Pricing
        {
            [QueueConsumer("pricing.get-quote")]
            public sealed class GetQuoteResponder : IRespond<GetQuote, Quote>
            {
                public ValueTask<Quote> RespondAsync(ConsumeContext<GetQuote> context, CancellationToken cancellationToken) =>
                    new(new Quote(1m));
            }
        }

        """;

    [Fact]
    public void should_warn_when_the_call_expects_another_response_than_the_responder_answers_with()
    {
        // given
        var source = _Usings + _Contracts + _Responder + _Caller("Pricing.OtherQuote");

        // when
        var driver = GeneratorTestHelper.Run(source);

        // then
        var diagnostic = _Single(driver);
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
        diagnostic
            .GetMessage(CultureInfo.InvariantCulture)
            .Should()
            .Contain("'Pricing.OtherQuote' for 'Pricing.GetQuote'")
            .And.Contain("with 'Pricing.Quote'");
        _Text(diagnostic).Should().Be("RequestAsync<global::Pricing.GetQuote, global::Pricing.OtherQuote>");
    }

    [Fact]
    public void should_not_warn_when_the_call_expects_the_response_the_responder_answers_with()
    {
        // given
        var source = _Usings + _Contracts + _Responder + _Caller("Pricing.Quote");

        // when
        var driver = GeneratorTestHelper.Run(source);

        // then
        _HM013(driver).Should().BeEmpty();
    }

    [Fact]
    public void should_not_warn_when_no_responder_for_the_request_is_visible()
    {
        // given
        var source = _Usings + _Contracts + _Caller("Pricing.OtherQuote");

        // when
        var driver = GeneratorTestHelper.Run(source);

        // then
        _HM013(driver).Should().BeEmpty();
    }

    [Fact]
    public void should_check_the_call_against_a_responder_published_by_a_referenced_assembly()
    {
        // given
        var responderAssembly = _GenerateResponderAssembly();
        var source = _Usings + _Caller("Pricing.OtherQuote");

        // when
        var driver = GeneratorTestHelper.Run("Checkout", source, [responderAssembly], out _, out _);

        // then
        var diagnostic = _Single(driver);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("with 'Pricing.Quote'");
    }

    [Fact]
    public void should_not_warn_when_a_referenced_responder_answers_with_the_expected_response()
    {
        // given
        var responderAssembly = _GenerateResponderAssembly();
        var source = _Usings + _Caller("Pricing.Quote");

        // when
        var driver = GeneratorTestHelper.Run("Checkout", source, [responderAssembly], out _, out _);

        // then
        _HM013(driver).Should().BeEmpty();
    }

    [Fact]
    public void should_check_a_conditional_call()
    {
        // given
        const string source =
            _Usings
            + _Contracts
            + _Responder
            + """
                namespace Checkout
                {
                    public sealed class Pricing(IRequestClient? requests)
                    {
                        public Task<global::Pricing.OtherQuote>? QuoteAsync() =>
                            requests?.RequestAsync<global::Pricing.GetQuote, global::Pricing.OtherQuote>(new global::Pricing.GetQuote("sku"));
                    }
                }
                """;

        // when
        var driver = GeneratorTestHelper.Run(source);

        // then
        _Single(driver);
    }

    [Fact]
    public void should_skip_a_call_whose_type_arguments_are_type_parameters()
    {
        // given
        const string source =
            _Usings
            + _Contracts
            + _Responder
            + """
                namespace Checkout
                {
                    public sealed class Requests(IRequestClient requests)
                    {
                        public Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request)
                            where TRequest : class
                            where TResponse : class => requests.RequestAsync<TRequest, TResponse>(request);
                    }
                }
                """;

        // when
        var driver = GeneratorTestHelper.Run(source);

        // then
        _HM013(driver).Should().BeEmpty();
    }

    [Fact]
    public void should_ignore_a_request_async_method_on_another_type()
    {
        // given
        const string source =
            _Usings
            + _Contracts
            + _Responder
            + """
                namespace Checkout
                {
                    public sealed class OtherClient
                    {
                        public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request) => Task.FromResult(default(TResponse)!);
                    }

                    public static class Caller
                    {
                        public static Task<global::Pricing.OtherQuote> QuoteAsync(OtherClient client) =>
                            client.RequestAsync<global::Pricing.GetQuote, global::Pricing.OtherQuote>(new global::Pricing.GetQuote("sku"));
                    }
                }
                """;

        // when
        var driver = GeneratorTestHelper.Run(source);

        // then
        _HM013(driver).Should().BeEmpty();
    }

    [Fact]
    public void should_publish_each_responder_as_assembly_metadata()
    {
        // given
        const string source = _Usings + _Contracts + _Responder;

        // when
        GeneratorTestHelper.Run(source, out var diagnostics, out var output);

        // then
        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        var metadata = output
            .Assembly.GetAttributes()
            .Should()
            .ContainSingle(attribute =>
                attribute.AttributeClass!.ToDisplayString() == "Headless.Messaging.ResponderMetadataAttribute"
            )
            .Which;
        metadata
            .ConstructorArguments.Select(argument => ((ITypeSymbol)argument.Value!).ToDisplayString())
            .Should()
            .Equal("Pricing.GetQuote", "Pricing.Quote");
    }

    /// <summary>
    /// A caller that requests a <c>Pricing.GetQuote</c> and expects <paramref name="responseType"/>; it names the contract
    /// types fully, so it compiles both beside the contracts and in an assembly that references them.
    /// </summary>
    private static string _Caller(string responseType) =>
        $$"""
            namespace Checkout
            {
                public sealed class CheckoutPricing(IRequestClient requests)
                {
                    public Task<global::{{responseType}}> QuoteAsync(CancellationToken cancellationToken) =>
                        requests.RequestAsync<global::Pricing.GetQuote, global::{{responseType}}>(new global::Pricing.GetQuote("sku"), cancellationToken: cancellationToken);
                }
            }
            """;

    /// <summary>Compiles the contracts and the responder, with the generator, into an assembly a caller can reference.</summary>
    private static MetadataReference _GenerateResponderAssembly()
    {
        GeneratorTestHelper.Run(
            "Pricing.Responders",
            _Usings + _Contracts + _Responder,
            [],
            out var diagnostics,
            out var output
        );
        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        return output.ToMetadataReference();
    }

    private static IEnumerable<Diagnostic> _HM013(GeneratorDriver driver) =>
        GeneratorTestHelper
            .GeneratorDiagnostics(driver)
            .Where(diagnostic => string.Equals(diagnostic.Id, "HM013", StringComparison.Ordinal));

    private static Diagnostic _Single(GeneratorDriver driver) => _HM013(driver).Should().ContainSingle().Which;

    private static string _Text(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);
}
