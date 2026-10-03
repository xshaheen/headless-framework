// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Tests;

/// <summary>
/// The declaration rules of the Messaging generator: what one consumer class registers, and every rule that fails or
/// warns at build time instead of at startup.
/// </summary>
public sealed class MessagingIncrementalSourceGeneratorTests
{
    private const string _Usings = """
        using System.Threading;
        using System.Threading.Tasks;
        using Headless.Messaging;

        """;

    private const string _PolicyUsing = """
        using Headless.Reliability;

        """;

    [Fact]
    public void should_register_one_consumer_entry_per_implemented_message_with_one_dispatcher()
    {
        // given
        const string source =
            _Usings
            + """
                namespace Billing;

                public sealed record InvoiceIssued(string Number);
                public sealed record InvoicePaid(string Number);

                [BusConsumer("billing.invoice-projection")]
                public sealed class InvoiceProjection : IConsume<InvoiceIssued>, IConsume<InvoicePaid>
                {
                    public ValueTask ConsumeAsync(ConsumeContext<InvoiceIssued> context, CancellationToken cancellationToken) => default;
                    public ValueTask ConsumeAsync(ConsumeContext<InvoicePaid> context, CancellationToken cancellationToken) => default;
                }
                """;

        // when
        var generated = _GenerateClean(source);

        // then
        _RegistrationLines(generated)
            .Should()
            .Equal(
                "catalog.AddBusConsumer<global::Billing.InvoiceProjection, global::Billing.InvoiceIssued>(\"billing.invoice-projection\", everyInstance: false, dispatch: Dispatch_Billing_InvoiceProjection);",
                "catalog.AddBusConsumer<global::Billing.InvoiceProjection, global::Billing.InvoicePaid>(\"billing.invoice-projection\", everyInstance: false, dispatch: Dispatch_Billing_InvoiceProjection);"
            );
        generated
            .Should()
            .Contain("case global::Headless.Messaging.ConsumeContext<global::Billing.InvoiceIssued> typed:")
            .And.Contain("case global::Headless.Messaging.ConsumeContext<global::Billing.InvoicePaid> typed:");
    }

    [Fact]
    public void should_record_the_every_instance_flag()
    {
        // given
        const string source =
            _Usings
            + """
                namespace Billing;

                public sealed record PriceChanged(string Sku);

                [BusConsumer("billing.price-cache", EveryInstance = true)]
                public sealed class PriceCache : IConsume<PriceChanged>, IOnSubscriptionEstablished
                {
                    public ValueTask ConsumeAsync(ConsumeContext<PriceChanged> context, CancellationToken cancellationToken) => default;
                    public ValueTask OnSubscriptionEstablishedAsync(SubscriptionEstablishedContext context, CancellationToken cancellationToken) => default;
                }
                """;

        // when
        var generated = _GenerateClean(source);

        // then
        _RegistrationLines(generated)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain(
                "(\"billing.price-cache\", everyInstance: true, dispatch: Dispatch_Billing_PriceCache, "
                    + "onSubscriptionEstablished: OnSubscriptionEstablished_Billing_PriceCache);"
            );
    }

    [Fact]
    public void should_register_a_queue_consumer_without_an_every_instance_argument()
    {
        // given
        const string source =
            _Usings
            + """
                namespace Billing;

                public sealed record IssueInvoiceCommand(string OrderId);

                [QueueConsumer("billing.issue-invoice")]
                public sealed class IssueInvoice : IConsume<IssueInvoiceCommand>
                {
                    public ValueTask ConsumeAsync(ConsumeContext<IssueInvoiceCommand> context, CancellationToken cancellationToken) => default;
                }
                """;

        // when
        var generated = _GenerateClean(source);

        // then
        _RegistrationLines(generated)
            .Should()
            .Equal(
                "catalog.AddQueueConsumer<global::Billing.IssueInvoice, global::Billing.IssueInvoiceCommand>(\"billing.issue-invoice\", dispatch: Dispatch_Billing_IssueInvoice);"
            );
    }

    [Fact]
    public void should_fail_at_the_attribute_when_the_class_implements_no_consume_interface()
    {
        // given
        const string source =
            _Usings
            + """
                namespace Billing;

                [QueueConsumer("billing.nothing")]
                public sealed class NotAConsumer;
                """;

        // when
        var driver = GeneratorTestHelper.Run(source);

        // then
        var diagnostic = _Single(driver, "HM003");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        _Text(diagnostic).Should().Be("QueueConsumer(\"billing.nothing\")");
        driver.GetRunResult().GeneratedTrees.Should().BeEmpty();
    }

    [Fact]
    public void should_fail_the_second_queue_consumer_for_one_message()
    {
        // given
        const string source =
            _Usings
            + """
                namespace Billing;

                public sealed record IssueInvoiceCommand(string OrderId);

                [QueueConsumer("billing.issue-invoice")]
                public sealed class IssueInvoice : IConsume<IssueInvoiceCommand>
                {
                    public ValueTask ConsumeAsync(ConsumeContext<IssueInvoiceCommand> context, CancellationToken cancellationToken) => default;
                }

                [QueueConsumer("billing.issue-invoice-again")]
                public sealed class IssueInvoiceAgain : IConsume<IssueInvoiceCommand>
                {
                    public ValueTask ConsumeAsync(ConsumeContext<IssueInvoiceCommand> context, CancellationToken cancellationToken) => default;
                }
                """;

        // when
        var driver = GeneratorTestHelper.Run(source);

        // then
        var diagnostic = _Single(driver, "HM004");
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("Billing.IssueInvoiceCommand");
        _Text(diagnostic).Should().Be("QueueConsumer(\"billing.issue-invoice-again\")");
        driver.GetRunResult().GeneratedTrees.Should().BeEmpty();
    }

    [Fact]
    public void should_allow_bus_consumers_for_a_message_that_has_a_queue_consumer()
    {
        // given
        const string source =
            _Usings
            + """
                namespace Billing;

                public sealed record InvoiceIssued(string Number);

                [QueueConsumer("billing.issue-invoice")]
                public sealed class QueueSide : IConsume<InvoiceIssued>
                {
                    public ValueTask ConsumeAsync(ConsumeContext<InvoiceIssued> context, CancellationToken cancellationToken) => default;
                }

                [BusConsumer("billing.projection")]
                public sealed class BusSide : IConsume<InvoiceIssued>
                {
                    public ValueTask ConsumeAsync(ConsumeContext<InvoiceIssued> context, CancellationToken cancellationToken) => default;
                }

                [BusConsumer("audit.projection")]
                public sealed class OtherBusSide : IConsume<InvoiceIssued>
                {
                    public ValueTask ConsumeAsync(ConsumeContext<InvoiceIssued> context, CancellationToken cancellationToken) => default;
                }
                """;

        // when
        var generated = _GenerateClean(source);

        // then
        _RegistrationLines(generated).Should().HaveCount(3);
    }

    [Theory]
    [InlineData("Identities.Value()", true)]
    [InlineData("Identities.Field", true)]
    [InlineData("\"billing\"", false)]
    [InlineData("\"billing.\"", false)]
    [InlineData("\" billing.projection\"", false)]
    [InlineData("null", false)]
    public void should_fail_an_identity_that_is_not_a_constant_in_owner_name_form(
        string identityExpression,
        bool isCompilerError
    )
    {
        // given
        var source =
            _Usings
            + $$"""
                namespace Billing;

                public static class Identities
                {
                    public static readonly string Field = "billing.projection";
                    public static string Value() => "billing.projection";
                }

                public sealed record InvoiceIssued(string Number);

                [BusConsumer({{identityExpression}})]
                public sealed class Projection : IConsume<InvoiceIssued>
                {
                    public ValueTask ConsumeAsync(ConsumeContext<InvoiceIssued> context, CancellationToken cancellationToken) => default;
                }
                """;

        // when
        var driver = GeneratorTestHelper.Run(source, out var diagnostics);

        // then
        _Single(driver, "HM001").Severity.Should().Be(DiagnosticSeverity.Error);
        driver.GetRunResult().GeneratedTrees.Should().BeEmpty();
        if (isCompilerError)
        {
            // CS0182: an attribute argument must be a constant expression.
            diagnostics.Should().Contain(diagnostic => diagnostic.Id == "CS0182");
        }
    }

    [Fact]
    public void should_fail_a_duplicate_identity_within_a_lane_and_allow_it_across_lanes()
    {
        // given
        const string duplicate =
            _Usings
            + """
                namespace Billing;

                public sealed record A(int Id);
                public sealed record B(int Id);

                [BusConsumer("billing.projection")]
                public sealed class First : IConsume<A>
                {
                    public ValueTask ConsumeAsync(ConsumeContext<A> context, CancellationToken cancellationToken) => default;
                }

                [BusConsumer("billing.projection")]
                public sealed class Second : IConsume<B>
                {
                    public ValueTask ConsumeAsync(ConsumeContext<B> context, CancellationToken cancellationToken) => default;
                }
                """;
        var acrossLanes = duplicate.Replace(
            "[BusConsumer(\"billing.projection\")]\npublic sealed class Second",
            "[QueueConsumer(\"billing.projection\")]\npublic sealed class Second",
            StringComparison.Ordinal
        );

        // when
        var duplicateDriver = GeneratorTestHelper.Run(duplicate);
        var acrossLanesDriver = GeneratorTestHelper.Run(acrossLanes, out var acrossLanesDiagnostics);

        // then
        var diagnostic = _Single(duplicateDriver, "HM002");
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("Bus").And.Contain("billing.projection");
        diagnostic
            .Location.SourceSpan.Start.Should()
            .Be(duplicate.LastIndexOf("BusConsumer(\"billing.projection\")", StringComparison.Ordinal));
        duplicateDriver.GetRunResult().GeneratedTrees.Should().BeEmpty();
        acrossLanes.Should().NotBe(duplicate);
        acrossLanesDiagnostics.Should().NotContain(x => x.Severity == DiagnosticSeverity.Error);
        acrossLanesDriver.GetRunResult().GeneratedTrees.Should().ContainSingle();
    }

    [Theory]
    [InlineData("BusConsumer(\"billing.cache\")")]
    [InlineData("QueueConsumer(\"billing.cache\")")]
    public void should_warn_when_a_competing_consumer_implements_the_subscription_hook(string attribute)
    {
        // given
        var source =
            _Usings
            + $$"""
                namespace Billing;

                public sealed record PriceChanged(string Sku);

                [{{attribute}}]
                public sealed class PriceCache : IConsume<PriceChanged>, IOnSubscriptionEstablished
                {
                    public ValueTask ConsumeAsync(ConsumeContext<PriceChanged> context, CancellationToken cancellationToken) => default;
                    public ValueTask OnSubscriptionEstablishedAsync(SubscriptionEstablishedContext context, CancellationToken cancellationToken) => default;
                }
                """;

        // when
        var driver = GeneratorTestHelper.Run(source, out var diagnostics);

        // then
        _Single(driver, "HM006").Severity.Should().Be(DiagnosticSeverity.Warning);
        diagnostics.Should().NotContain(x => x.Severity == DiagnosticSeverity.Error);
        driver.GetRunResult().GeneratedTrees.Should().ContainSingle("a warning does not stop registration");
    }

    [Fact]
    public void should_fail_a_consumer_class_or_message_type_the_generated_code_cannot_name()
    {
        // given
        const string source =
            _Usings
            + """
                namespace Billing;

                public sealed record Visible(int Id);

                public sealed class Outer
                {
                    [BusConsumer("billing.hidden")]
                    private sealed class Hidden : IConsume<Visible>
                    {
                        public ValueTask ConsumeAsync(ConsumeContext<Visible> context, CancellationToken cancellationToken) => default;
                    }
                }

                [BusConsumer("billing.private-message")]
                public sealed class PrivateMessage : IConsume<PrivateMessage.Secret>
                {
                    private sealed record Secret(int Id);

                    ValueTask IConsume<Secret>.ConsumeAsync(ConsumeContext<Secret> context, CancellationToken cancellationToken) => default;
                }
                """;

        // when
        var driver = GeneratorTestHelper.Run(source);

        // then
        GeneratorTestHelper
            .GeneratorDiagnostics(driver)
            .Where(x => string.Equals(x.Id, "HM007", StringComparison.Ordinal))
            .Select(x => x.GetMessage(CultureInfo.InvariantCulture))
            .Should()
            .SatisfyRespectively(
                first => first.Should().Contain("Billing.Outer.Hidden"),
                second => second.Should().Contain("Billing.PrivateMessage.Secret")
            );
        driver.GetRunResult().GeneratedTrees.Should().BeEmpty();
    }

    [Fact]
    public void should_register_an_accessible_nested_consumer_by_its_qualified_name()
    {
        // given
        const string source =
            _Usings
            + """
                namespace Billing;

                public sealed record Visible(int Id);

                public static class Handlers
                {
                    [BusConsumer("billing.nested")]
                    internal sealed class Nested : IConsume<Visible>
                    {
                        public ValueTask ConsumeAsync(ConsumeContext<Visible> context, CancellationToken cancellationToken) => default;
                    }
                }
                """;

        // when
        var generated = _GenerateClean(source);

        // then
        _RegistrationLines(generated)
            .Should()
            .Equal(
                "catalog.AddBusConsumer<global::Billing.Handlers.Nested, global::Billing.Visible>(\"billing.nested\", everyInstance: false, dispatch: Dispatch_Billing_Handlers_Nested);"
            );
    }

    [Theory]
    [InlineData("public abstract class Projection")]
    [InlineData("public sealed class Projection<T>")]
    public void should_fail_an_abstract_or_generic_consumer_class(string declaration)
    {
        // given
        var source =
            _Usings
            + $$"""
                namespace Billing;

                public sealed record Visible(int Id);

                [BusConsumer("billing.projection")]
                {{declaration}} : IConsume<Visible>
                {
                    public ValueTask ConsumeAsync(ConsumeContext<Visible> context, CancellationToken cancellationToken) => default;
                }
                """;

        // when
        var driver = GeneratorTestHelper.Run(source);

        // then
        _Single(driver, "HM008").Severity.Should().Be(DiagnosticSeverity.Error);
        driver.GetRunResult().GeneratedTrees.Should().BeEmpty();
    }

    [Fact]
    public void should_fail_a_class_that_declares_both_lanes()
    {
        // given
        const string source =
            _Usings
            + """
                namespace Billing;

                public sealed record Visible(int Id);

                [BusConsumer("billing.projection")]
                [QueueConsumer("billing.command")]
                public sealed class BothLanes : IConsume<Visible>
                {
                    public ValueTask ConsumeAsync(ConsumeContext<Visible> context, CancellationToken cancellationToken) => default;
                }
                """;

        // when
        var driver = GeneratorTestHelper.Run(source);

        // then
        _Single(driver, "HM009").GetMessage(CultureInfo.InvariantCulture).Should().Contain("Billing.BothLanes");
        driver.GetRunResult().GeneratedTrees.Should().BeEmpty();
    }

    [Theory]
    [InlineData("QueueConsumer(\"billing.charge\", FailurePolicy = typeof(PaymentsPolicy))", "AddQueueConsumer")]
    [InlineData("BusConsumer(\"billing.charge\", FailurePolicy = typeof(PaymentsPolicy))", "AddBusConsumer")]
    public void should_register_a_declared_failure_policy_as_a_factory(string attribute, string method)
    {
        // given
        var source =
            _Usings
            + _PolicyUsing
            + $$"""
                namespace Billing;

                public sealed record ChargeCard(string OrderId);

                public sealed class PaymentsPolicy : FailurePolicy
                {
                    protected override void Configure(FailurePolicyBuilder policy) => policy.Immediate(retries: 2);
                }

                [{{attribute}}]
                public sealed class Charge : IConsume<ChargeCard>
                {
                    public ValueTask ConsumeAsync(ConsumeContext<ChargeCard> context, CancellationToken cancellationToken) => default;
                }
                """;

        // when
        var generated = _GenerateClean(source);

        // then
        _RegistrationLines(generated)
            .Should()
            .ContainSingle()
            .Which.Should()
            .StartWith($"catalog.{method}<global::Billing.Charge, global::Billing.ChargeCard>(")
            .And.EndWith(
                "dispatch: Dispatch_Billing_Charge, failurePolicy: static () => new global::Billing.PaymentsPolicy());"
            );
    }

    [Theory]
    [InlineData("public sealed class Policy { }", "typeof(Policy)")]
    [InlineData("public sealed class Policy { }", "typeof(Policy[])")]
    [InlineData(
        "public abstract class Policy : FailurePolicy { protected override void Configure(FailurePolicyBuilder policy) { } }",
        "typeof(Policy)"
    )]
    [InlineData(
        "public sealed class Policy<T> : FailurePolicy { protected override void Configure(FailurePolicyBuilder policy) { } }",
        "typeof(Policy<>)"
    )]
    [InlineData(
        "public sealed class Policy : FailurePolicy { private Policy() { } protected override void Configure(FailurePolicyBuilder policy) { } }",
        "typeof(Policy)"
    )]
    [InlineData(
        "public sealed class Policy : FailurePolicy { public Policy(int retries) { } protected override void Configure(FailurePolicyBuilder policy) { } }",
        "typeof(Policy)"
    )]
    [InlineData(
        "file sealed class Policy : FailurePolicy { protected override void Configure(FailurePolicyBuilder policy) { } }",
        "typeof(Policy)"
    )]
    public void should_fail_a_failure_policy_type_the_generated_factory_cannot_construct(
        string declaration,
        string policy
    )
    {
        // given
        var source =
            _Usings
            + _PolicyUsing
            + $$"""
                namespace Billing;

                public sealed record ChargeCard(string OrderId);

                {{declaration}}

                [QueueConsumer("billing.charge", FailurePolicy = {{policy}})]
                public sealed class Charge : IConsume<ChargeCard>
                {
                    public ValueTask ConsumeAsync(ConsumeContext<ChargeCard> context, CancellationToken cancellationToken) => default;
                }
                """;

        // when
        var driver = GeneratorTestHelper.Run(source);

        // then
        var diagnostic = _Single(driver, "HM005");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("Billing.").And.Contain("Charge");
        driver.GetRunResult().GeneratedTrees.Should().BeEmpty();
    }

    [Fact]
    public void should_fail_a_failure_policy_on_an_every_instance_consumer()
    {
        // given
        const string source =
            _Usings
            + _PolicyUsing
            + """
                namespace Billing;

                public sealed record PriceChanged(string Sku);

                public sealed class CachePolicy : FailurePolicy
                {
                    protected override void Configure(FailurePolicyBuilder policy) => policy.Immediate(retries: 2);
                }

                [BusConsumer("billing.price-cache", EveryInstance = true, FailurePolicy = typeof(CachePolicy))]
                public sealed class PriceCache : IConsume<PriceChanged>
                {
                    public ValueTask ConsumeAsync(ConsumeContext<PriceChanged> context, CancellationToken cancellationToken) => default;
                }
                """;

        // when
        var driver = GeneratorTestHelper.Run(source);

        // then
        var diagnostic = _Single(driver, "HM010");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Contain("PriceCache");
        GeneratorTestHelper.GeneratorDiagnostics(driver).Should().NotContain(x => x.Id == "HM005");
        driver.GetRunResult().GeneratedTrees.Should().BeEmpty();
    }

    [Fact]
    public void should_emit_nothing_for_an_assembly_without_consumers()
    {
        // when
        var driver = GeneratorTestHelper.Run(_Usings + "namespace Billing; public sealed class Plain;");

        // then
        driver.GetRunResult().GeneratedTrees.Should().BeEmpty();
        GeneratorTestHelper.GeneratorDiagnostics(driver).Should().BeEmpty();
    }

    [Fact]
    public void should_use_the_messaging_diagnostic_prefix_for_every_descriptor()
    {
        var descriptorsType =
            typeof(Headless.Messaging.SourceGenerator.MessagingIncrementalSourceGenerator).Assembly.GetType(
                "Headless.Messaging.SourceGenerator.Validation.DiagnosticDescriptors"
            );

        descriptorsType.Should().NotBeNull();

        descriptorsType!
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.GetValue(null))
            .OfType<DiagnosticDescriptor>()
            .Select(descriptor => descriptor.Id)
            .Should()
            .BeEquivalentTo(Enumerable.Range(1, 10).Select(number => $"HM{number:000}"));
    }

    private static string _GenerateClean(string source)
    {
        var driver = GeneratorTestHelper.Run(source, out var diagnostics);
        diagnostics.Should().NotContain(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        return driver.GetRunResult().GeneratedTrees.Should().ContainSingle().Which.ToString();
    }

    private static string[] _RegistrationLines(string generated) =>
        [
            .. generated
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("catalog.Add", StringComparison.Ordinal)),
        ];

    private static Diagnostic _Single(GeneratorDriver driver, string id) =>
        GeneratorTestHelper.GeneratorDiagnostics(driver).Should().ContainSingle(x => x.Id == id).Which;

    private static string _Text(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);
}
