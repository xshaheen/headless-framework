// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Idempotency;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Tests;

public sealed class WithIdempotencyEndpointMetadataTests
{
    [Fact]
    public void should_attach_idempotency_metadata_via_extension()
    {
        var builder = new RecordingBuilder();

        builder.WithIdempotency(o => o.IdempotencyKeyExpiration = TimeSpan.FromDays(7));

        builder.CapturedMetadata.Should().ContainSingle();
        builder.CapturedMetadata[0].Should().BeOfType<IdempotencyMetadata>();

        var probe = new IdempotencyOptions();
        ((IdempotencyMetadata)builder.CapturedMetadata[0]).Configure(probe);
        probe.IdempotencyKeyExpiration.Should().Be(TimeSpan.FromDays(7));
    }

    [Fact]
    public void should_append_metadata_when_called_twice()
    {
        var builder = new RecordingBuilder();

        builder.WithIdempotency(o => o.IdempotencyKeyExpiration = TimeSpan.FromHours(1));
        builder.WithIdempotency(o => o.IdempotencyKeyExpiration = TimeSpan.FromHours(2));

        builder.CapturedMetadata.Should().HaveCount(2);

        // Verify the LAST entry carries the second config (GetMetadata<T> returns last)
        var probe = new IdempotencyOptions();
        ((IdempotencyMetadata)builder.CapturedMetadata[1]).Configure(probe);
        probe.IdempotencyKeyExpiration.Should().Be(TimeSpan.FromHours(2));
    }

    private sealed class RecordingBuilder : IEndpointConventionBuilder
    {
        public List<object> CapturedMetadata { get; } = [];

        public void Add(Action<EndpointBuilder> convention)
        {
            var b = new StubEndpointBuilder();
            convention(b);
            CapturedMetadata.AddRange(b.Metadata);
        }

        private sealed class StubEndpointBuilder : EndpointBuilder
        {
            public override Endpoint Build() =>
                new(
                    RequestDelegate ?? (_ => Task.CompletedTask),
                    new EndpointMetadataCollection(Metadata),
                    DisplayName
                );
        }
    }
}
