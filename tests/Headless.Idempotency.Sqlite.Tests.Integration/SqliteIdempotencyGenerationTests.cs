// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Hosting;
using Headless.Idempotency;
using Headless.Sql.Sqlite;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;

namespace Tests;

/// <summary>
/// SQLite has no sequence object: the admission's grant writes the next generation into the record, and a trigger on
/// that column raises the one-row generation table. The token an admission returns comes from the same statement's
/// RETURNING, so it must be the value the row stored, or every later fence by that token is refused.
/// </summary>
[Collection<SqliteIdempotencyFixture>]
public sealed class SqliteIdempotencyGenerationTests(SqliteIdempotencyFixture fixture) : TestBase
{
    private static readonly IdempotencyFingerprint _Fingerprint = IdempotencyFingerprint.Compute("request");

    private static readonly string _Records = SqliteDialect.Instance.Qualify(
        HeadlessStorageDefaults.Schema,
        "idempotency_records"
    );

    [Fact]
    public async Task should_return_the_stored_lease_generation_from_an_admission_and_complete_with_it()
    {
        var key = $"generation-{Guid.NewGuid():N}";
        await using var host = await fixture.CreateHostAsync(cancellationToken: AbortToken);

        var first = await host.Operations.AdmitAsync(key, _Fingerprint, cancellationToken: AbortToken);
        (await _ReadLeaseGenerationAsync(key)).Should().Be(first.Generation);

        // A takeover draws again through the trigger, and its token must again be the stored value.
        await fixture.ShiftLeaseIntoPastAsync(new IdempotencyRecordKey("", key), TimeSpan.FromDays(2), AbortToken);
        var takeover = await host.Operations.AdmitAsync(key, _Fingerprint, cancellationToken: AbortToken);
        takeover.IsTakeover.Should().BeTrue();
        takeover.Generation.Should().BeGreaterThan(first.Generation!.Value);
        (await _ReadLeaseGenerationAsync(key)).Should().Be(takeover.Generation);

        await host.Operations.CompleteAsync(
            takeover,
            Encoding.UTF8.GetBytes("done"),
            "contract/v1",
            cancellationToken: AbortToken
        );

        var replay = await host.Operations.AdmitAsync(key, _Fingerprint, cancellationToken: AbortToken);
        replay.Disposition.Should().Be(IdempotentDisposition.Replay);
        Encoding.UTF8.GetString(replay.Result!.Payload.Span).Should().Be("done");
    }

    private async Task<long> _ReadLeaseGenerationAsync(string key)
    {
        await using var connection = (SqliteConnection)fixture.CreateConnection();
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT lease_generation FROM {_Records} WHERE tenant_id = '' AND idempotency_key = @recordKey;";
        command.Parameters.AddWithValue("recordKey", key);

        return (long)(await command.ExecuteScalarAsync(AbortToken))!;
    }
}
