// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails;

/// <summary>
/// Reads and manages the emails that development senders captured instead of delivering. Registered only when a
/// development sender (<c>UseDevelopment</c>) is in use, so resolve it with <c>GetService</c> to detect one.
/// </summary>
/// <remarks>
/// <para>
/// The store keeps the newest <see cref="Capacity"/> emails in memory, shared by every development email sender in
/// the process, and drops the oldest when it is full. Nothing is persisted: a restart empties it.
/// </para>
/// <para>
/// Every change (a capture, a delete, or a clear) increments <see cref="Version"/>. To follow new emails without
/// re-reading the whole list, read <see cref="Version"/>, call <see cref="WaitForChangeAsync"/> with it, then
/// <see cref="Query"/> with <see cref="EmailCaptureQuery.AfterSequence"/> set to the version you last read.
/// </para>
/// <para>All members are thread-safe.</para>
/// </remarks>
[PublicAPI]
public interface IEmailCaptureStore
{
    /// <summary>Gets the maximum number of emails the store keeps.</summary>
    int Capacity { get; }

    /// <summary>Gets the number of emails the store holds now.</summary>
    int Count { get; }

    /// <summary>Gets the change counter, which increases on every capture, delete, and clear.</summary>
    long Version { get; }

    /// <summary>Lists the captured emails that match <paramref name="query"/>, newest first.</summary>
    /// <param name="query">The filters to apply, or <see langword="null"/> to list every email.</param>
    /// <returns>A snapshot of the matching emails.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="EmailCaptureQuery.Limit"/> is zero or negative.</exception>
    IReadOnlyList<CapturedEmail> Query(EmailCaptureQuery? query = null);

    /// <summary>Gets the captured email with the given <see cref="CapturedEmail.Id"/>.</summary>
    /// <param name="id">The capture identifier.</param>
    /// <returns>The email, or <see langword="null"/> when the store does not hold it.</returns>
    CapturedEmail? Get(Guid id);

    /// <summary>Removes the captured email with the given <see cref="CapturedEmail.Id"/>.</summary>
    /// <param name="id">The capture identifier.</param>
    /// <returns><see langword="true"/> when the email was removed; <see langword="false"/> when the store did not hold it.</returns>
    bool Delete(Guid id);

    /// <summary>Removes every captured email.</summary>
    /// <returns>The number of emails removed.</returns>
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

/// <summary>Filters a <see cref="IEmailCaptureStore.Query"/> call. Every filter left <see langword="null"/> matches all emails.</summary>
[PublicAPI]
public sealed record EmailCaptureQuery
{
    /// <summary>
    /// Gets the sender instance name to match exactly. An empty string matches the default (unnamed) sender.
    /// </summary>
    public string? SenderName { get; init; }

    /// <summary>
    /// Gets text to find, ignoring case, in the subject, the bodies, or any address or display name.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Gets a <see cref="CapturedEmail.Sequence"/>; only emails captured after it match.</summary>
    public long? AfterSequence { get; init; }

    /// <summary>Gets the maximum number of emails to return. Must be positive.</summary>
    public int? Limit { get; init; }
}
