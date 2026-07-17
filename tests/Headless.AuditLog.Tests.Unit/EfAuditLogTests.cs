// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.AuditLog;
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
        IOptions<AuditLogOptions>? options = null
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
            options ?? Options.Create(new AuditLogOptions())
        );
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
            var act = () => sut.LogAsync(new("user.login"), cancellationToken: cts.Token);

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
            await sut.LogAsync(new("user.login"), cancellationToken: AbortToken);

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
    public async Task should_reject_non_positive_read_query_limit(int limit)
    {
        // given
        var dbFactory = Substitute.For<IDbContextFactory<AuditStoreDbContext>>();
        var sut = new EfReadAuditLog<AuditStoreDbContext>(dbFactory);

        // when
        var act = () => sut.QueryAsync(new() { Limit = limit }, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
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
                new(action)
                {
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
        var request = new AuditLogWriteRequest("pii.revealed")
        {
            EntityId = "customer-secret",
            Data = new(StringComparer.Ordinal) { ["email"] = "private@example.com" },
        };

        request.ToString().Should().Be(nameof(AuditLogWriteRequest));
    }
}
