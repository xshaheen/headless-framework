// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Headless.DistributedLocks.SqlServer;

#pragma warning disable CA2100 // Sequence identifiers are validated, sanitized, and quoted before interpolation.
internal sealed class SqlServerFencingTokenSource(IOptions<SqlServerDistributedLockOptions> options)
    : IFencingTokenSource,
        IDisposable
{
    private readonly SemaphoreSlim _ensureGate = new(1, 1);
    private bool _sequenceEnsured;

    public async ValueTask<long?> NextAsync(string resource, CancellationToken cancellationToken = default)
    {
        if (!options.Value.EnableFencing)
        {
            return null;
        }

        await using var connection = options.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await _EnsureSequenceAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = (int)options.Value.CommandTimeout.TotalSeconds;
        command.CommandText =
            $"SELECT NEXT VALUE FOR {SqlServerIdentifier.Quote(options.Value.Schema)}.{SqlServerIdentifier.Quote(SqlServerIdentifier.FenceSequenceName(options.Value.KeyPrefix))}";

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private async ValueTask _EnsureSequenceAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _sequenceEnsured))
        {
            return;
        }

        await _ensureGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_sequenceEnsured)
            {
                return;
            }

            await SqlServerDistributedLocksStorageInitializer.EnsureSequenceAsync(connection, options.Value, cancellationToken)
                .ConfigureAwait(false);
            Volatile.Write(ref _sequenceEnsured, true);
        }
        finally
        {
            _ensureGate.Release();
        }
    }

    public void Dispose()
    {
        _ensureGate.Dispose();
    }
}
#pragma warning restore CA2100
