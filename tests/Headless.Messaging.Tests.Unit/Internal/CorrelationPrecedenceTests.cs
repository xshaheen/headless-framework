// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Context;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;

namespace Tests.Internal;

public sealed class CorrelationPrecedenceTests : TestBase
{
    [Fact]
    public void should_preserve_captured_root_without_mutating_ambient_business_context()
    {
        var ambient = _ConsumeContext("unrelated-root");
        var accessor = new AsyncLocalConsumeContextAccessor { Current = ambient };
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns("unrelated-tenant");
        var factory = _CreateFactory(accessor: accessor, tenant: tenant);
        const string traceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00";

        var prepared = factory.Create(
            new TestMessage(null),
            new PublishOptions
            {
                MessageId = "captured-root",
                SuppressAmbientBusinessContext = true,
                Headers = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.TraceParent] = traceParent,
                },
            }
        );

        prepared.Message.Headers[Headers.CorrelationId].Should().Be("captured-root");
        prepared.Message.Headers.Should().NotContainKey(Headers.CausationId);
        prepared.Message.Headers.Should().NotContainKey(Headers.TenantId);
        prepared.Message.Headers[Headers.TraceParent].Should().Be(traceParent);
        accessor.Current.Should().BeSameAs(ambient);
        tenant.Id.Should().Be("unrelated-tenant");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void should_preserve_explicit_business_context_and_registered_contract_when_ambient_suppression_changes(
        bool suppress
    )
    {
        var accessor = new AsyncLocalConsumeContextAccessor { Current = _ConsumeContext("unrelated-root") };
        var factory = _CreateFactory(accessor: accessor, requireTenant: true);

        var prepared = factory.Create(
            new TestMessage(null),
            new PublishOptions
            {
                MessageId = "captured-id",
                CorrelationId = "captured-root",
                CausationId = "captured-parent",
                TenantId = "captured-tenant",
                SuppressAmbientBusinessContext = suppress,
            }
        );

        prepared.Message.Headers[Headers.MessageId].Should().Be("captured-id");
        prepared.Message.Headers[Headers.CorrelationId].Should().Be("captured-root");
        prepared.Message.Headers[Headers.CausationId].Should().Be("captured-parent");
        prepared.Message.Headers[Headers.TenantId].Should().Be("captured-tenant");
        prepared.Message.Headers[Headers.MessageName].Should().Be("test.message");
    }

    [Fact]
    public void should_reject_captured_system_scope_under_required_tenancy_instead_of_borrowing_current_tenant()
    {
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns("unrelated-tenant");
        var factory = _CreateFactory(tenant: tenant, requireTenant: true);

        factory.Create(new TestMessage(null)).Message.Headers[Headers.TenantId].Should().Be("unrelated-tenant");
        var act = () =>
            factory.Create(new TestMessage(null), new PublishOptions { SuppressAmbientBusinessContext = true });

        act.Should().Throw<MissingTenantContextException>();
    }

    [Fact]
    public void should_prefer_explicit_correlation_over_selector()
    {
        // given
        var factory = _CreateFactory(selector: static message => message.Correlation);

        // when
        var prepared = factory.Create(
            new TestMessage("selector-correlation"),
            new PublishOptions { CorrelationId = "explicit-correlation" }
        );

        // then
        prepared.Message.Headers[Headers.CorrelationId].Should().Be("explicit-correlation");
    }

    [Fact]
    public void should_use_selector_correlation_when_explicit_correlation_is_absent()
    {
        // given
        var factory = _CreateFactory(selector: static message => message.Correlation);

        // when
        var prepared = factory.Create(new TestMessage("selector-correlation"));

        // then
        prepared.Message.Headers[Headers.CorrelationId].Should().Be("selector-correlation");
    }

    [Theory]
    [InlineData("explicit\r\ncorrelation", "explicit")]
    [InlineData("selector\r\ncorrelation", "selector")]
    [InlineData("ambient\r\ncorrelation", "ambient")]
    public void should_reject_correlation_values_with_control_characters(string correlationId, string source)
    {
        // given
        var accessor = string.Equals(source, "ambient", StringComparison.Ordinal)
            ? new AsyncLocalConsumeContextAccessor { Current = _ConsumeContext(correlationId) }
            : null;
        var factory = string.Equals(source, "selector", StringComparison.Ordinal)
            ? _CreateFactory(selector: _ => correlationId)
            : _CreateFactory(accessor: accessor);
        var options = string.Equals(source, "explicit", StringComparison.Ordinal)
            ? new PublishOptions { CorrelationId = correlationId }
            : null;

        // when
        var act = () => factory.Create(new TestMessage(null), options);

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*headless-corr-id*control characters*");
    }

    [Fact]
    public void should_prefer_selector_correlation_over_ambient()
    {
        // given
        var accessor = new AsyncLocalConsumeContextAccessor { Current = _ConsumeContext("ambient-value") };
        var factory = _CreateFactory(selector: static message => message.Correlation, accessor: accessor);

        // when
        var prepared = factory.Create(new TestMessage("selector-value"));

        // then
        prepared.Message.Headers[Headers.CorrelationId].Should().Be("selector-value");
    }

    [Fact]
    public void should_use_ambient_correlation_when_explicit_and_selector_are_absent()
    {
        // given
        var accessor = new AsyncLocalConsumeContextAccessor { Current = _ConsumeContext("ambient-correlation") };
        var factory = _CreateFactory(accessor: accessor);

        // when
        var prepared = factory.Create(new TestMessage(null));

        // then
        prepared.Message.Headers[Headers.CorrelationId].Should().Be("ambient-correlation");
    }

    [Fact]
    public void should_default_correlation_to_message_id_when_no_source_is_available()
    {
        // given
        var factory = _CreateFactory();

        // when
        var prepared = factory.Create(new TestMessage(null));

        // then
        prepared.Message.Headers[Headers.CorrelationId].Should().Be(prepared.Message.Headers[Headers.MessageId]);
    }

    [Fact]
    public void should_use_ambient_message_id_as_causation_without_changing_root_correlation()
    {
        // given
        var accessor = new AsyncLocalConsumeContextAccessor { Current = _ConsumeContext("root-correlation") };
        var factory = _CreateFactory(accessor: accessor);

        // when
        var prepared = factory.Create(new TestMessage(null));

        // then
        prepared.Message.Headers[Headers.CorrelationId].Should().Be("root-correlation");
        prepared.Message.Headers[Headers.CausationId].Should().Be("source-message");
    }

    [Fact]
    public void should_prefer_explicit_causation_over_ambient_message_id()
    {
        // given
        var accessor = new AsyncLocalConsumeContextAccessor { Current = _ConsumeContext("root-correlation") };
        var factory = _CreateFactory(accessor: accessor);

        // when
        var prepared = factory.Create(new TestMessage(null), new PublishOptions { CausationId = "explicit-cause" });

        // then
        prepared.Message.Headers[Headers.CausationId].Should().Be("explicit-cause");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("v 2")]
    [InlineData("2\r\ninvalid")]
    public void should_reject_invalid_explicit_message_contract_versions(string version)
    {
        // given
        var factory = _CreateFactory();

        // when
        var act = () => factory.Create(new TestMessage(null), new PublishOptions { ContractVersion = version });

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_not_invoke_selector_for_null_payload()
    {
        // given
        var invoked = false;
        var factory = _CreateFactory(selector: message =>
        {
            invoked = true;
            return message.Correlation;
        });

        // when
        var prepared = factory.Create<TestMessage>(null);

        // then
        invoked.Should().BeFalse();
        prepared.Message.Headers[Headers.CorrelationId].Should().Be(prepared.Message.Headers[Headers.MessageId]);
    }

    [Fact]
    public void should_treat_empty_selector_result_as_absent()
    {
        // given
        var accessor = new AsyncLocalConsumeContextAccessor { Current = _ConsumeContext("ambient-correlation") };
        var factory = _CreateFactory(selector: static _ => " ", accessor: accessor);

        // when
        var prepared = factory.Create(new TestMessage("ignored"));

        // then
        prepared.Message.Headers[Headers.CorrelationId].Should().Be("ambient-correlation");
    }

    [Fact]
    public void should_leave_traceparent_header_unchanged()
    {
        // given
        const string traceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00";
        var factory = _CreateFactory(selector: static message => message.Correlation);

        // when
        var prepared = factory.Create(
            new TestMessage("selector-correlation"),
            new PublishOptions
            {
                Headers = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.TraceParent] = traceParent,
                },
            }
        );

        // then
        prepared.Message.Headers[Headers.TraceParent].Should().Be(traceParent);
    }

    [Fact]
    public void should_wrap_selector_failure_with_message_type()
    {
        // given
        var factory = _CreateFactory(selector: static _ => throw new InvalidOperationException("boom"));

        // when
        var act = () => factory.Create(new TestMessage("ignored"));

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*CorrelationFrom selector failed*TestMessage*")
            .WithInnerException<InvalidOperationException>();
    }

    [Fact]
    public void should_take_correlation_and_causation_from_the_causal_scope_of_a_running_job()
    {
        // given a publish inside a job run and outside any consumer
        var factory = _CreateFactory();
        using var scope = CausalScope.Begin("order-7", "run-1");

        // when
        var prepared = factory.Create(new TestMessage(null));

        // then
        prepared.Message.Headers[Headers.CorrelationId].Should().Be("order-7");
        prepared.Message.Headers[Headers.CausationId].Should().Be("run-1");
    }

    [Fact]
    public void should_keep_the_selector_correlation_and_take_the_causal_scope_causation()
    {
        // given
        var factory = _CreateFactory(selector: static message => message.Correlation);
        using var scope = CausalScope.Begin("order-7", "run-1");

        // when
        var prepared = factory.Create(new TestMessage("selector-correlation"));

        // then
        prepared.Message.Headers[Headers.CorrelationId].Should().Be("selector-correlation");
        prepared.Message.Headers[Headers.CausationId].Should().Be("run-1");
    }

    [Fact]
    public void should_ignore_the_causal_scope_when_ambient_business_context_is_suppressed()
    {
        // given
        var factory = _CreateFactory();
        using var scope = CausalScope.Begin("order-7", "run-1");

        // when
        var prepared = factory.Create(
            new TestMessage(null),
            new PublishOptions { SuppressAmbientBusinessContext = true }
        );

        // then
        prepared.Message.Headers[Headers.CorrelationId].Should().Be(prepared.Message.Headers[Headers.MessageId]);
        prepared.Message.Headers.Should().NotContainKey(Headers.CausationId);
    }

    [Fact]
    public void should_prefer_explicit_ids_over_the_causal_scope()
    {
        // given
        var factory = _CreateFactory();
        using var scope = CausalScope.Begin("order-7", "run-1");

        // when
        var prepared = factory.Create(
            new TestMessage(null),
            new PublishOptions { CorrelationId = "explicit-correlation", CausationId = "explicit-cause" }
        );

        // then
        prepared.Message.Headers[Headers.CorrelationId].Should().Be("explicit-correlation");
        prepared.Message.Headers[Headers.CausationId].Should().Be("explicit-cause");
    }

    [Fact]
    public void should_prefer_the_consumed_message_over_the_causal_scope_inside_a_consumer()
    {
        // given the consume pipeline opens both the consume context and a scope keyed by the received row
        var accessor = new AsyncLocalConsumeContextAccessor { Current = _ConsumeContext("root-correlation") };
        var factory = _CreateFactory(accessor: accessor);
        using var scope = CausalScope.Begin("root-correlation", "received-row");

        // when
        var prepared = factory.Create(new TestMessage(null));

        // then
        prepared.Message.Headers[Headers.CorrelationId].Should().Be("root-correlation");
        prepared.Message.Headers[Headers.CausationId].Should().Be("source-message");
    }

    [Fact]
    public void should_start_its_own_correlation_when_the_consumed_correlation_is_longer_than_the_limit()
    {
        // given a received foreign correlation id this host cannot index
        var accessor = new AsyncLocalConsumeContextAccessor
        {
            Current = _ConsumeContext(new string('c', CausalScope.IdMaxLength + 50)),
        };
        var factory = _CreateFactory(accessor: accessor);

        // when
        var prepared = factory.Create(new TestMessage(null));

        // then
        prepared.Message.Headers[Headers.CorrelationId].Should().Be(prepared.Message.Headers[Headers.MessageId]);
        prepared.Message.Headers[Headers.CausationId].Should().Be("source-message");
    }

    [Theory]
    [InlineData("explicit-correlation")]
    [InlineData("selector")]
    [InlineData("explicit-causation")]
    public void should_refuse_a_lineage_id_longer_than_the_limit(string source)
    {
        // given
        var tooLong = new string('c', CausalScope.IdMaxLength + 1);
        var factory = string.Equals(source, "selector", StringComparison.Ordinal)
            ? _CreateFactory(selector: _ => tooLong)
            : _CreateFactory();
        var options = source switch
        {
            "explicit-correlation" => new PublishOptions { CorrelationId = tooLong },
            "explicit-causation" => new PublishOptions { CausationId = tooLong },
            _ => null,
        };

        // when
        var act = () => factory.Create(new TestMessage(null), options);

        // then
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*200 characters or fewer*");
    }

    private static MessagePublishRequestFactory _CreateFactory(
        Func<TestMessage, string?>? selector = null,
        IConsumeContextAccessor? accessor = null,
        ICurrentTenant? tenant = null,
        bool requireTenant = false
    )
    {
        var registry = new ConsumerRegistry();
        registry.RegisterMessageName(typeof(TestMessage), "test.message");
        var registrations = selector is null
            ? Array.Empty<MessageRegistration>()
            :
            [
                new MessageRegistration(
                    typeof(TestMessage),
                    MessageLane.Bus,
                    "test.message",
                    message => selector((TestMessage)message),
                    new Dictionary<Type, object>(),
                    []
                ),
            ];

        return new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            Options.Create(new MessagingOptions { TenantContextRequired = requireTenant }),
            registry,
            tenant ?? new NullCurrentTenant(),
            new MessageMetadataRegistry(registrations),
            accessor
        );
    }

    private static ConsumeContext<TestMessage> _ConsumeContext(string correlationId)
    {
        return new()
        {
            Message = new TestMessage(null),
            MessageId = "source-message",
            CorrelationId = correlationId,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            MessageName = "source",
            Lane = MessageLane.Bus,
        };
    }

    private sealed record TestMessage(string? Correlation);
}
