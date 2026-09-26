// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Headless.Checks;

namespace Headless.Idempotency;

/// <summary>
/// Typed JSON helpers over the byte-oriented idempotency API. They take source-generated
/// <see cref="JsonTypeInfo{T}" /> metadata rather than reflection options, so they work under trimming and native AOT.
/// </summary>
[PublicAPI]
[SuppressMessage(
    "Naming",
    "CA1708:Identifiers should differ by more than case",
    Justification = "C# 14 extension member blocks emit compiler-generated marker members differing only by case."
)]
public static class IdempotentOperationsJsonExtensions
{
    extension(IIdempotentOperations operations)
    {
        /// <summary>Serializes <paramref name="result" /> as UTF-8 JSON and completes the admitted operation with it.</summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="admission">The admitted operation.</param>
        /// <param name="result">The result.</param>
        /// <param name="typeInfo">The result type's JSON metadata.</param>
        /// <param name="contract">The contract tag the JSON is written under.</param>
        /// <param name="retention">How long the result replays; the admission's retention when <see langword="null" />.</param>
        /// <param name="cancellationToken">Token used to cancel the database calls before the commit.</param>
        /// <returns>A task that completes when the result is committed.</returns>
        /// <exception cref="ArgumentNullException">An argument is <see langword="null" />.</exception>
        /// <exception cref="StaleAdmissionException">The attempt no longer owns the key; nothing was stored.</exception>
        public ValueTask CompleteAsync<T>(
            IdempotentAdmission admission,
            T result,
            JsonTypeInfo<T> typeInfo,
            string contract,
            TimeSpan? retention = null,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(operations);
            Argument.IsNotNull(typeInfo);

            var payload = JsonSerializer.SerializeToUtf8Bytes(result, typeInfo);

            return operations.CompleteAsync(admission, payload, contract, retention, cancellationToken);
        }
    }

    extension(UnitOfWorkIdempotency idempotency)
    {
        /// <summary>
        /// Serializes <paramref name="result" /> as UTF-8 JSON and completes the admitted operation with it inside the
        /// bound unit's transaction.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="admission">The admitted operation.</param>
        /// <param name="result">The result.</param>
        /// <param name="typeInfo">The result type's JSON metadata.</param>
        /// <param name="contract">The contract tag the JSON is written under.</param>
        /// <param name="retention">How long the result replays; the admission's retention when <see langword="null" />.</param>
        /// <param name="cancellationToken">Token used to cancel the database commands.</param>
        /// <returns>A task that completes when the result is written.</returns>
        /// <exception cref="ArgumentNullException">An argument is <see langword="null" />.</exception>
        /// <exception cref="StaleAdmissionException">The attempt no longer owns the key; nothing was written.</exception>
        public ValueTask CompleteAsync<T>(
            IdempotentAdmission admission,
            T result,
            JsonTypeInfo<T> typeInfo,
            string contract,
            TimeSpan? retention = null,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(idempotency);
            Argument.IsNotNull(typeInfo);

            var payload = JsonSerializer.SerializeToUtf8Bytes(result, typeInfo);

            return idempotency.CompleteAsync(admission, payload, contract, retention, cancellationToken);
        }
    }

    extension(IdempotentResult result)
    {
        /// <summary>Deserializes the stored UTF-8 JSON result.</summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="typeInfo">The result type's JSON metadata.</param>
        /// <returns>The result.</returns>
        /// <exception cref="ArgumentNullException">An argument is <see langword="null" />.</exception>
        /// <exception cref="JsonException">The stored bytes are not valid JSON for <typeparamref name="T" />.</exception>
        public T? Deserialize<T>(JsonTypeInfo<T> typeInfo)
        {
            Argument.IsNotNull(result);
            Argument.IsNotNull(typeInfo);

            return JsonSerializer.Deserialize(result.Payload.Span, typeInfo);
        }
    }
}
