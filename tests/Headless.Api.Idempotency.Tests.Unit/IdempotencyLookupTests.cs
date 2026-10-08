// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Api;
using Headless.Api.Idempotency;
using Headless.Context;
using Headless.Http;
using Headless.Idempotency;
using Headless.MultiTenancy;
using Headless.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class IdempotencyLookupTests : IdempotencyMiddlewareTestBase
{
    private static readonly IdempotentRequestTarget _Target = new("post", "/transactions", "k1");

    private static readonly string _StoreKey = IdempotencyMiddleware.HashScope("idem:u1:POST:/transactions:k1");

    [Fact]
    public async Task should_read_the_status_of_the_record_the_middleware_stored_for_the_earlier_request()
    {
        // given
        var operations = Substitute.For<IIdempotentOperations>();
        operations.PeekAsync(_StoreKey, AbortToken).Returns(IdempotencyPeekStatus.Completed);
        var lookup = _CreateLookup(operations);

        // when
        var status = await lookup.GetStatusAsync(CreateContext(), _Target, AbortToken);

        // then
        status.Should().Be(IdempotencyPeekStatus.Completed);
    }

    [Fact]
    public async Task should_replay_the_stored_response_of_a_completed_earlier_request()
    {
        // given
        var operations = Substitute.For<IIdempotentOperations>();
        var snapshot = new IdempotencyResponseSnapshot
        {
            StatusCode = StatusCodes.Status201Created,
            Headers = { ["Location"] = ["/transactions/9"], ["X-Secret"] = ["not replayed"] },
            Body = [1, 2, 3],
        };
        operations.GetResultAsync(_StoreKey, AbortToken).Returns(_Result(snapshot));
        var lookup = _CreateLookup(operations);
        var context = CreateContext(method: "GET", path: "/transactions/k1");

        // when
        var replayed = await lookup.TryReplayAsync(context, _Target, AbortToken);

        // then
        replayed.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status201Created);
        context.Response.Headers.Location.ToString().Should().Be("/transactions/9");
        context.Response.Headers.Should().NotContainKey("X-Secret", "only allowlisted headers replay");
        context.Response.Headers[HttpHeaderNames.IdempotentReplayed].ToString().Should().Be("true");
        ((MemoryStream)context.Response.Body).ToArray().Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task should_leave_the_response_untouched_when_there_is_no_stored_response()
    {
        // given
        var operations = Substitute.For<IIdempotentOperations>();
        operations.GetResultAsync(_StoreKey, AbortToken).Returns((IdempotentResult?)null);
        var lookup = _CreateLookup(operations);
        var context = CreateContext(method: "GET");

        // when
        var replayed = await lookup.TryReplayAsync(context, _Target, AbortToken);

        // then
        replayed.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task should_address_the_query_string_of_the_earlier_request()
    {
        // given
        var operations = Substitute.For<IIdempotentOperations>();
        var lookup = _CreateLookup(operations);
        var target = _Target with { Query = new QueryString("?mode=capture") };

        // when
        await lookup.GetStatusAsync(CreateContext(), target, AbortToken);

        // then
        await operations
            .Received(1)
            .PeekAsync(IdempotencyMiddleware.HashScope("idem:u1:POST:/transactions?mode=capture:k1"), AbortToken);
    }

    [Fact]
    public async Task should_find_nothing_for_an_anonymous_caller_while_user_identity_is_required()
    {
        // given
        var operations = Substitute.For<IIdempotentOperations>();
        var anonymous = Substitute.For<ICurrentUser>();
        anonymous.UserId.Returns((UserId?)null);
        var lookup = _CreateLookup(operations, currentUser: anonymous);

        // when
        var status = await lookup.GetStatusAsync(CreateContext(), _Target, AbortToken);
        var replayed = await lookup.TryReplayAsync(CreateContext(), _Target, AbortToken);

        // then
        status.Should().Be(IdempotencyPeekStatus.Absent);
        replayed.Should().BeFalse();
        operations.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("bad\nkey")]
    public async Task should_reject_a_blank_or_malformed_key_before_the_store(string key)
    {
        // given
        var operations = Substitute.For<IIdempotentOperations>();
        var lookup = _CreateLookup(operations);

        // when
        var read = async () => await lookup.GetStatusAsync(CreateContext(), _Target with { Key = key }, AbortToken);

        // then
        await read.Should().ThrowAsync<ArgumentException>();
        operations.ReceivedCalls().Should().BeEmpty();
    }

    private static IdempotencyLookup _CreateLookup(IIdempotentOperations operations, ICurrentUser? currentUser = null)
    {
        if (currentUser is null)
        {
            currentUser = Substitute.For<ICurrentUser>();
            currentUser.UserId.Returns(new UserId("u1"));
        }

        return new IdempotencyLookup(
            Monitor(new IdempotencyOptions()),
            operations,
            Substitute.For<ICurrentTenant>(),
            currentUser,
            Options.Create(new MultiTenancyOptions())
        );
    }

    private static IdempotentResult _Result(IdempotencyResponseSnapshot snapshot)
    {
        return new IdempotentResult(
            JsonSerializer.SerializeToUtf8Bytes(snapshot, IdempotencyJsonContext.Default.IdempotencyResponseSnapshot),
            IdempotencyResponseSnapshot.Contract
        );
    }
}
