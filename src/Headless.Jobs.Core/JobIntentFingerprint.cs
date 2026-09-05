// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Headless.Jobs;

internal static class JobIntentFingerprint
{
    internal const string Algorithm = "v1";

    internal static void Validate<TJob>(TJob job)
        where TJob : TimeJobEntity<TJob>
    {
        JobContract.ValidateName(job.Function);
        JobContract.ValidateVersion(job.ContractVersion);
        _ = new JobKeyScope(job.Function, job.TenantId);
        if (job.ParentId is not null || job.Children.Count != 0 || job.RunCondition is not null)
        {
            throw new NotSupportedException(
                "Keyed JobChain scheduling and control are unsupported. A JobChain is a static conditional continuation tree; schedule keyed work as a standalone time job."
            );
        }

        if (
            job.Status != JobStatus.Idle
            || job.OwnerId is not null
            || job.LockedUntil is not null
            || job.CancelRequested
            || job.ExecutedAt is not null
            || job.RetryCount != 0
        )
        {
            throw new ArgumentException(
                "A keyed candidate must be new, pending, unclaimed work with no consumed attempts.",
                nameof(job)
            );
        }

        if (job.ExecutionTime is not { Kind: DateTimeKind.Utc })
        {
            throw new ArgumentException(
                "Keyed scheduling requires an absolute UTC instant; use the DateTimeOffset scheduler surface.",
                nameof(job)
            );
        }

        if (
            job.Retries < 0
            || job.RetryIntervals?.Any(static value => value < 0) == true
            || !Enum.IsDefined(job.OnNodeDeath)
        )
        {
            throw new ArgumentException(
                "Keyed scheduling requires nonnegative retry counts and intervals and a known node-death policy.",
                nameof(job)
            );
        }
    }

    internal static void Normalize<TJob>(TJob job)
        where TJob : TimeJobEntity<TJob>
    {
        Validate(job);
        var due = job.ExecutionTime!.Value;
        job.ExecutionTime = new DateTime(due.Ticks - (due.Ticks % 10), DateTimeKind.Utc);
        job.Request = job.Request?.ToArray();
        job.RetryIntervals = job.RetryIntervals is { Length: > 0 } intervals ? intervals.ToArray() : null;
    }

#pragma warning disable MA0045 // Pure in-memory canonical hashing has no asynchronous I/O; synchronous MemoryStream disposal is intentional.
    internal static string Compute<TJob>(TJob job, string algorithm)
        where TJob : TimeJobEntity<TJob>
    {
        if (!string.Equals(algorithm, Algorithm, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Unknown stored Jobs intent fingerprint algorithm '{algorithm}'. Migrate or explicitly replace that generation; it cannot be reinterpreted implicitly."
            );
        }

        // v1: BinaryWriter little-endian integers; strings/bytes use signed Int32 UTF-8 byte lengths (-1 for null).
        // An explicit tag separates this encoding from any later algorithm. Payload bytes are never deserialized.
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true);
        _WriteBytes(writer, "headless-jobs-intent-v1"u8.ToArray());
        _WriteBytes(writer, Encoding.UTF8.GetBytes(job.ContractVersion));
        _WriteBytes(writer, job.Request);
        writer.Write(job.ExecutionTime!.Value.Ticks);
        writer.Write(job.Retries);
        writer.Write(job.RetryIntervals?.Length ?? 0);
        foreach (var interval in job.RetryIntervals ?? [])
        {
            writer.Write(interval);
        }

        writer.Write((int)job.OnNodeDeath);
        writer.Flush();
        return Convert.ToHexStringLower(SHA256.HashData(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length))));
    }
#pragma warning restore MA0045

    private static void _WriteBytes(BinaryWriter writer, byte[]? bytes)
    {
        writer.Write(bytes?.Length ?? -1);
        if (bytes is not null)
        {
            writer.Write(bytes);
        }
    }

    internal static void RejectOrdinaryMutation<TJob>(TJob job)
        where TJob : TimeJobEntity<TJob>
    {
        var pending = new Stack<TJob>();
        var visited = new HashSet<TJob>(ReferenceEqualityComparer.Instance);
        pending.Push(job);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            if (
                current.BusinessKey is not null
                || current.IntentFingerprint is not null
                || current.FingerprintAlgorithm is not null
                || current.Generation is not null
                || current.IsCurrentGeneration is not null
            )
            {
                throw new InvalidOperationException(
                    "Keyed jobs and every historical generation are retained indefinitely. Ordinary add, update, reset, retry, and delete cannot change them; use generation-fenced keyed control."
                );
            }

            foreach (var child in current.Children)
            {
                pending.Push(child);
            }
        }
    }

    internal static JobScheduleResult Result<TJob>(TJob? job, JobScheduleDisposition disposition)
        where TJob : TimeJobEntity<TJob> => new(disposition, job?.Id, job?.Generation, job?.Status);
}
