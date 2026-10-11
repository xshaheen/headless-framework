// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;

namespace Tests.Transport;

public sealed class TransportEnvelopeGateTests : TestBase
{
    private const string _RawName = "runs.result";
    private const string _StrictName = "orders.placed";
    private static readonly DateTimeOffset _StoredAt = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly TransportEnvelopeGate _gate = new([_Route(_RawName, "2", raw: true), _Route(_StrictName, "1")]);

    [Fact]
    public void should_complete_a_headerless_delivery_on_a_raw_route_from_the_arrival_and_broker_metadata()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);

        // when
        _gate.Admit(headers, _Arrival(_RawName), state: 0, static _ => new("producer-7", "nats:S:4:9", _StoredAt));

        // then
        headers[Headers.MessageName].Should().Be(_RawName);
        headers[Headers.MessageId].Should().Be("producer-7");
        headers[Headers.ContractVersion].Should().Be("2");
        headers[Headers.CorrelationId].Should().Be("producer-7");
        headers[Headers.SentTime].Should().Be(_StoredAt.ToString("O", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void should_use_the_position_when_the_producer_sent_no_identity()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);

        // when
        _gate.Admit(headers, _Arrival(_RawName), state: 0, static _ => new(null, "kafka:t:0:42:1", null));

        // then
        headers[Headers.MessageId].Should().Be("kafka:t:0:42:1");
        headers.Should().NotContainKey(Headers.SentTime);
    }

    [Fact]
    public void should_keep_every_header_the_producer_sent()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "m-1",
            [Headers.ContractVersion] = "2",
            [Headers.CorrelationId] = "c-1",
            [Headers.SentTime] = "2026-01-01T00:00:00.0000000+00:00",
        };
        var factsRead = 0;

        // when
        _gate.Admit(
            headers,
            _Arrival(_RawName),
            state: 0,
            _ =>
            {
                factsRead++;
                return new("other", "other", _StoredAt);
            }
        );

        // then
        headers[Headers.MessageId].Should().Be("m-1");
        headers[Headers.CorrelationId].Should().Be("c-1");
        headers[Headers.SentTime].Should().Be("2026-01-01T00:00:00.0000000+00:00");
        factsRead.Should().Be(0);
    }

    [Fact]
    public void should_reject_a_header_that_names_another_contract_on_a_raw_route()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageName] = _StrictName,
            [Headers.MessageId] = "m-1",
        };

        // when
        var admit = () => _gate.Admit(headers, _Arrival(_RawName));

        // then
        admit.Should().Throw<InvalidDataException>().WithMessage($"*'{_StrictName}'*'{_RawName}'*");
    }

    [Fact]
    public void should_reject_a_raw_delivery_the_broker_gives_no_identity()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);

        // when
        var admit = () => _gate.Admit(headers, _Arrival(_RawName), state: 0, static _ => default);

        // then
        admit.Should().Throw<InvalidDataException>().WithMessage("*no message id*");
    }

    [Fact]
    public void should_keep_the_strict_check_on_a_route_that_is_not_raw()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);

        // when
        var admit = () => _gate.Admit(headers, _Arrival(_StrictName), state: 0, static _ => new("p", "p", _StoredAt));

        // then
        admit.Should().Throw<InvalidDataException>().WithMessage("*missing a required Messaging header*");
        headers.Should().BeEmpty();
    }

    [Fact]
    public void should_route_by_header_on_a_route_that_is_not_raw()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageName] = "orders.other",
            [Headers.MessageId] = "m-1",
        };

        // when
        var admit = () => _gate.Admit(headers, _Arrival(_StrictName));

        // then
        admit.Should().NotThrow();
    }

    [Fact]
    public void should_complete_by_header_name_when_the_address_names_no_message()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = _RawName };

        // when
        _gate.Admit(headers, _Arrival(arrivalName: null), state: 0, static _ => new(null, "sqs:abc", null));

        // then
        headers[Headers.MessageId].Should().Be("sqs:abc");
    }

    [Fact]
    public void should_not_complete_by_header_name_when_the_address_names_a_route_that_is_not_raw()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageName] = _RawName };

        // when
        var admit = () => _gate.Admit(headers, _Arrival(_StrictName), state: 0, static _ => new(null, "p", null));

        // then
        admit.Should().Throw<InvalidDataException>().WithMessage("*missing a required Messaging header*");
    }

    [Fact]
    public void should_strip_control_characters_from_a_producer_name_in_the_reason()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageName] = "forged\nINFO admin login",
            [Headers.MessageId] = "m-1",
        };

        // when
        var admit = () => _gate.Admit(headers, _Arrival(_RawName));

        // then
        admit.Should().Throw<InvalidDataException>().Which.Message.Should().NotContain("\n");
    }

    [Fact]
    public void should_reject_a_message_id_over_the_storage_limit()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageName] = _StrictName,
            [Headers.MessageId] = new string('x', MessageOptions.MessageIdMaxLength + 1),
        };

        // when
        var admit = () => _gate.Admit(headers, _Arrival(_StrictName));

        // then
        admit.Should().Throw<InvalidDataException>().WithMessage("*longer than*");
    }

    [Fact]
    public void should_require_headers_on_an_authoritative_address_whose_route_is_not_raw()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        var arrival = _Arrival(_StrictName) with { AddressIsAuthoritative = true };

        // when
        var admit = () => _gate.Admit(headers, arrival);

        // then
        admit.Should().Throw<InvalidDataException>().WithMessage("*missing a required Messaging header*");
    }

    [Fact]
    public void should_take_the_tenant_from_the_address()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        var arrival = _Arrival(_RawName) with { AddressTenant = "tenant-a" };

        // when
        _gate.Admit(headers, arrival, state: 0, static _ => new(null, "p", null));

        // then
        headers[Headers.TenantId].Should().Be("tenant-a");
    }

    [Fact]
    public void should_reject_a_tenant_header_that_differs_from_the_address_tenant()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.TenantId] = "tenant-b" };
        var arrival = _Arrival(_RawName) with { AddressTenant = "tenant-a" };

        // when
        var admit = () => _gate.Admit(headers, arrival, state: 0, static _ => new(null, "p", null));

        // then
        admit.Should().Throw<InvalidDataException>().WithMessage("*tenant header differs*");
    }

    [Fact]
    public void should_reject_an_address_without_its_expected_tenant()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        var arrival = _Arrival(_RawName) with { AddressTenantError = "token 4 is missing" };

        // when
        var admit = () => _gate.Admit(headers, arrival, state: 0, static _ => new(null, "p", null));

        // then
        admit.Should().Throw<InvalidDataException>().WithMessage("*token 4 is missing*");
    }

    [Fact]
    public void should_hash_a_position_longer_than_the_message_id_limit_deterministically()
    {
        // given
        var longTopic = new string('t', 240);

        // when
        var first = NativeEnvelopeFacts.Position("kafka", longTopic, "0", "42");
        var second = NativeEnvelopeFacts.Position("kafka", longTopic, "0", "42");

        // then
        first.Should().StartWith("kafka:sha256:").And.Be(second);
        first.Length.Should().BeLessThanOrEqualTo(MessageOptions.MessageIdMaxLength);
        NativeEnvelopeFacts.Position("kafka", "orders", "0", "42").Should().Be("kafka:orders:0:42");
    }

    private static EnvelopeArrival _Arrival(string? arrivalName) => new("Test", MessageLane.Queue, arrivalName);

    private static MessageMetadata _Route(string name, string version, bool raw = false) =>
        new(
            new MessageRouteKey(typeof(object), name, MessageLane.Queue),
            typeof(object),
            version,
            CorrelationSelector: null,
            new Dictionary<Type, object>(),
            RawPayload: raw
        );
}
