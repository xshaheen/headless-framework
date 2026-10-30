// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Context;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class EfAuditLogTests : TestBase
{
    private static readonly DateTimeOffset _Timestamp = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static EfAuditLog<AuditStoreDbContext> _CreateSut(
        AuditStoreDbContext db,
        IOptions<AuditLogOptions>? options = null,
        IAuditClientInfoProvider? clientInfo = null
    )
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var currentTenant = Substitute.For<ICurrentTenant>();
        var correlationIdProvider = Substitute.For<ICorrelationIdProvider>();
        var timeProvider = new FakeTimeProvider(_Timestamp);

        return new EfAuditLog<AuditStoreDbContext>(
            db,
            currentUser,
            currentTenant,
            correlationIdProvider,
            timeProvider,
            options ?? Options.Create(new AuditLogOptions()),
            clientInfo
        );
    }

    [Fact]
    public async Task should_fill_client_info_from_provider_when_request_leaves_it_null()
    {
        // given
        var (db, conn) = AuditStoreDbContext.Create();
        await using (conn)
        await using (db)
        {
            var clientInfo = Substitute.For<IAuditClientInfoProvider>();
            clientInfo.IpAddress.Returns("203.0.113.7");
            clientInfo.UserAgent.Returns("agent/1.0");
            var sut = _CreateSut(db, clientInfo: clientInfo);

            // when
            await sut.LogAsync(new() { Action = "pii.revealed" }, cancellationToken: AbortToken);

            // then
            var entry = db.ChangeTracker.Entries<AuditLogEntry>().Single().Entity;
            entry.IpAddress.Should().Be("203.0.113.7");
            entry.UserAgent.Should().Be("agent/1.0");
        }
    }

    [Fact]
    public async Task should_keep_request_client_info_and_truncate_it_to_column_limits()
    {
        // given
        var (db, conn) = AuditStoreDbContext.Create();
        await using (conn)
        await using (db)
        {
            var clientInfo = Substitute.For<IAuditClientInfoProvider>();
            clientInfo.IpAddress.Returns("203.0.113.7");
            clientInfo.UserAgent.Returns("agent/1.0");
            var sut = _CreateSut(db, clientInfo: clientInfo);
            var longAgent = new string('a', 600);

            // when
            await sut.LogAsync(
                new()
                {
                    Action = "pii.revealed",
                    IpAddress = "2001:db8::1",
                    UserAgent = longAgent,
                },
                cancellationToken: AbortToken
            );

            // then
            var entry = db.ChangeTracker.Entries<AuditLogEntry>().Single().Entity;
            entry.IpAddress.Should().Be("2001:db8::1");
            entry.UserAgent.Should().Be(longAgent[..512]);
        }
    }

    [Fact]
    public async Task should_leave_client_info_null_without_provider_or_request_value()
    {
        // given
        var (db, conn) = AuditStoreDbContext.Create();
        await using (conn)
        await using (db)
        {
            var sut = _CreateSut(db);

            // when
            await sut.LogAsync(new() { Action = "pii.revealed" }, cancellationToken: AbortToken);

            // then
            var entry = db.ChangeTracker.Entries<AuditLogEntry>().Single().Entity;
            entry.IpAddress.Should().BeNull();
            entry.UserAgent.Should().BeNull();
        }
    }

    [Fact]
    public async Task should_throw_operation_canceled_before_reading_options_when_token_is_pre_canceled()
    {
        // given - options that blow up if the IsEnabled check runs before the cancellation check
        var (db, conn) = AuditStoreDbContext.Create();
        await using (conn)
        await using (db)
        {
            var options = Substitute.For<IOptions<AuditLogOptions>>();
            options.Value.Returns(_ => throw new InvalidOperationException("IsEnabled was read before cancellation"));
            var sut = _CreateSut(db, options);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            // when
            var act = () => sut.LogAsync(new() { Action = "user.login" }, cancellationToken: cts.Token);

            // then - cancellation wins over the IsEnabled short-circuit and nothing is tracked
            await act.Should().ThrowAsync<OperationCanceledException>();
            db.ChangeTracker.Entries<AuditLogEntry>().Should().BeEmpty();
        }
    }

    [Fact]
    public async Task should_not_track_any_entry_when_audit_logging_is_disabled()
    {
        // given
        var (db, conn) = AuditStoreDbContext.Create();
        await using (conn)
        await using (db)
        {
            var sut = _CreateSut(db, Options.Create(new AuditLogOptions { IsEnabled = false }));

            // when
            await sut.LogAsync(new() { Action = "user.login" }, cancellationToken: AbortToken);

            // then
            db.ChangeTracker.Entries<AuditLogEntry>().Should().BeEmpty();
        }
    }

    [Fact]
    public async Task should_reject_null_read_query()
    {
        // given
        var dbFactory = Substitute.For<IDbContextFactory<AuditStoreDbContext>>();
        var sut = new EfReadAuditLog<AuditStoreDbContext>(dbFactory);

        // when
        var act = () => sut.QueryAsync(null!, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task should_reject_out_of_range_read_query_size(int size)
    {
        // given
        var dbFactory = Substitute.For<IDbContextFactory<AuditStoreDbContext>>();
        var sut = new EfReadAuditLog<AuditStoreDbContext>(dbFactory);

        // when
        var act = () => sut.QueryAsync(new() { Size = size }, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("AgAAAAAAAAAAAAAAAAAAAAA")] // unknown version byte
    [InlineData("AQAAAAAAAAAAAAAAAAAAAAAAAA")] // one byte too long
    public async Task should_reject_malformed_continuation_token(string token)
    {
        // given
        var dbFactory = Substitute.For<IDbContextFactory<AuditStoreDbContext>>();
        var sut = new EfReadAuditLog<AuditStoreDbContext>(dbFactory);

        // when
        var act = () => sut.QueryAsync(new() { ContinuationToken = token }, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("query");
        await dbFactory.DidNotReceive().CreateDbContextAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_reject_read_query_with_both_action_and_action_prefix()
    {
        // given
        var dbFactory = Substitute.For<IDbContextFactory<AuditStoreDbContext>>();
        var sut = new EfReadAuditLog<AuditStoreDbContext>(dbFactory);

        // when
        var act = () =>
            sut.QueryAsync(new() { Action = "order.created", ActionPrefix = "order." }, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("query");
        await dbFactory.DidNotReceive().CreateDbContextAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_reject_read_query_with_empty_action_prefix()
    {
        // given
        var dbFactory = Substitute.For<IDbContextFactory<AuditStoreDbContext>>();
        var sut = new EfReadAuditLog<AuditStoreDbContext>(dbFactory);

        // when
        var act = () => sut.QueryAsync(new() { ActionPrefix = "" }, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("query");
        await dbFactory.DidNotReceive().CreateDbContextAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_reject_undefined_read_query_direction()
    {
        // given
        var dbFactory = Substitute.For<IDbContextFactory<AuditStoreDbContext>>();
        var sut = new EfReadAuditLog<AuditStoreDbContext>(dbFactory);

        // when
        var act = () => sut.QueryAsync(new() { Direction = (AuditLogSortDirection)7 }, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_truncate_over_limit_fields_when_logging_explicit_event()
    {
        // given - values longer than the AuditLogFieldLimits column limits
        // (Action=256, EntityType=512, EntityId=256, ErrorCode=256)
        var (db, conn) = AuditStoreDbContext.Create();
        await using (conn)
        await using (db)
        {
            var sut = _CreateSut(db);

            var action = new string('a', 300);
            var entityType = new string('b', 600);
            var entityId = new string('c', 300);
            var errorCode = new string('d', 300);

            // when
            await sut.LogAsync(
                new()
                {
                    Action = action,
                    EntityType = entityType,
                    EntityId = entityId,
                    Success = false,
                    ErrorCode = errorCode,
                },
                cancellationToken: AbortToken
            );

            // then - the tracked entity holds the truncated values so inserts cannot exceed column DDL
            var entity = db.ChangeTracker.Entries<AuditLogEntry>().Should().ContainSingle().Which.Entity;
            entity.Action.Should().Be(action[..256]);
            entity.EntityType.Should().Be(entityType[..512]);
            entity.EntityId.Should().Be(entityId[..256]);
            entity.ErrorCode.Should().Be(errorCode[..256]);
            entity.Success.Should().BeFalse();
            entity.CreatedAt.Should().Be(_Timestamp.UtcDateTime);
        }
    }

    [Fact]
    public void should_not_include_audit_payload_in_request_string()
    {
        var request = new AuditLogWriteRequest
        {
            Action = "pii.revealed",
            EntityId = "customer-secret",
            Data = new(StringComparer.Ordinal) { ["email"] = "private@example.com" },
        };

        request.ToString().Should().Be(nameof(AuditLogWriteRequest));
    }
}
