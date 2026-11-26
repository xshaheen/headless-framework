// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms;

/// <summary>
/// Reads and manages the SMS messages that development senders captured instead of delivering. Registered only when a
/// development sender (<c>UseDevelopment</c>) is in use, so resolve it with <c>GetService</c> to detect one.
/// </summary>
/// <remarks>
/// <para>
/// The store keeps the newest <see cref="Capacity"/> messages in memory, shared by every development SMS sender in
/// the process, and drops the oldest when it is full. Nothing is persisted: a restart empties it.
/// </para>
/// <para>
/// Every change (a capture, a delete, or a clear) increments <see cref="Version"/>. To follow new messages without
/// re-reading the whole list, read <see cref="Version"/>, call <see cref="WaitForChangeAsync"/> with it, then
/// <see cref="Query"/> with <see cref="SmsCaptureQuery.AfterSequence"/> set to the version you last read.
/// </para>
/// <para>All members are thread-safe.</para>
/// </remarks>
[PublicAPI]
public interface ISmsCaptureStore
{
    /// <summary>Gets the maximum number of messages the store keeps.</summary>
    int Capacity { get; }

    /// <summary>Gets the number of messages the store holds now.</summary>
    int Count { get; }

    /// <summary>Gets the change counter, which increases on every capture, delete, and clear.</summary>
    long Version { get; }

    /// <summary>Lists the captured messages that match <paramref name="query"/>, newest first.</summary>
    /// <param name="query">The filters to apply. Pass <c>new()</c> to list every item.</param>
    /// <returns>A snapshot of the matching messages.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="SmsCaptureQuery.Limit"/> is zero or negative.</exception>
    IReadOnlyList<CapturedSms> Query(SmsCaptureQuery query);

    /// <summary>Gets the captured message with the given <see cref="CapturedSms.Id"/>.</summary>
    /// <param name="id">The capture identifier.</param>
    /// <returns>The message, or <see langword="null"/> when the store does not hold it.</returns>
    CapturedSms? Get(Guid id);

    /// <summary>Removes the captured message with the given <see cref="CapturedSms.Id"/>.</summary>
    /// <param name="id">The capture identifier.</param>
    /// <returns><see langword="true"/> when the message was removed; <see langword="false"/> when the store did not hold it.</returns>
    bool Delete(Guid id);

    /// <summary>Removes every captured message.</summary>
    /// <returns>The number of messages removed.</returns>
    int Clear();

    /// <summary>
    /// Waits until <see cref="Version"/> differs from <paramref name="knownVersion"/>, and returns at once when it
    /// already does.
    /// </summary>
    /// <param name="knownVersion">The version the caller last read.</param>
    /// <param name="cancellationToken">A token to stop waiting.</param>
    /// <returns>The current version.</returns>
    /// <exception cref="OperationCanceledException">The wait was canceled.</exception>
    ValueTask<long> WaitForChangeAsync(long knownVersion, CancellationToken cancellationToken = default);
}

/// <summary>Filters a <see cref="ISmsCaptureStore.Query"/> call. Every filter left <see langword="null"/> matches all messages.</summary>
[PublicAPI]
public sealed record SmsCaptureQuery
{
    /// <summary>
    /// Gets the sender instance name to match exactly. An empty string matches the default (unnamed) sender.
    /// </summary>
    public string? SenderName { get; init; }

    /// <summary>
    /// Gets text to find, ignoring case, in the message text, the message id, or any recipient number.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Gets a <see cref="CapturedSms.Sequence"/>; only messages captured after it match.</summary>
    public long? AfterSequence { get; init; }

    /// <summary>Gets the maximum number of messages to return. Must be positive.</summary>
    public int? Limit { get; init; }
}
