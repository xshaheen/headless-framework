// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Idempotency;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// SQLite refuses an admission inside a caller's unit, because SQLite has no counter that survives the caller's
/// rollback: the generation a rolled-back admission drew would be drawn again. Everything else the unit can do with
/// an autonomously admitted attempt stays supported.
/// </summary>
[Collection<SqliteIdempotencyFixture>]
public sealed class SqliteIdempotencyEnlistedAdmissionTests(SqliteIdempotencyFixture fixture) : TestBase
{
    private static readonly IdempotencyFingerprint _Fingerprint = IdempotencyFingerprint.Compute("request");

    [Fact]
    public async Task should_refuse_an_enlisted_admission_before_writing_and_name_the_reason()
    {
        var key = $"enlisted-{Guid.NewGuid():N}";
        await using var host = await fixture.CreateHostAsync(cancellationToken: AbortToken);
        await using var unit = await fixture.BeginUnitAsync(host, AbortToken);

        var act = async () => await unit.Unit.Idempotency.AdmitAsync(key, _Fingerprint, cancellationToken: AbortToken);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should()
            .Contain("survives the caller's rollback");
        unit.Unit.State.Should().Be(UnitOfWorkState.Active, "the refusal comes before the unit is touched");
        await unit.CommitAsync(AbortToken);
        (await fixture.ReadRecordAsync(new IdempotencyRecordKey("", key), AbortToken))
            .Should()
            .BeNull("the refusal wrote nothing");
    }

    [Fact]
    public async Task should_complete_an_autonomous_admission_inside_a_unit()
    {
        var key = $"autonomous-{Guid.NewGuid():N}";
        await using var host = await fixture.CreateHostAsync(cancellationToken: AbortToken);
        var admitted = await host.Operations.AdmitAsync(key, _Fingerprint, cancellationToken: AbortToken);

        await using (var unit = await fixture.BeginUnitAsync(host, AbortToken))
        {
            await unit.Unit.Idempotency.FenceAsync(admitted, AbortToken);
            await unit.Unit.Idempotency.CompleteAsync(
                admitted,
                Encoding.UTF8.GetBytes("done"),
                "contract/v1",
                cancellationToken: AbortToken
            );
            await unit.CommitAsync(AbortToken);
        }

        var replay = await host.Operations.AdmitAsync(key, _Fingerprint, cancellationToken: AbortToken);

        replay.Disposition.Should().Be(IdempotentDisposition.Replay);
        Encoding.UTF8.GetString(replay.Result!.Payload.Span).Should().Be("done");
    }
}
