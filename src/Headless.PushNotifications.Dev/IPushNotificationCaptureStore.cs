// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications;

/// <summary>
/// Reads and manages the push notifications that development services captured instead of delivering. Registered only when a
/// development service (<c>UseDevelopment</c>) is in use, so resolve it with <c>GetService</c> to detect one.
/// </summary>
/// <remarks>
/// <para>
/// The store keeps the newest <see cref="Capacity"/> notifications in memory, shared by every development push notification service in
/// the process, and drops the oldest when it is full. Nothing is persisted: a restart empties it.
/// </para>
/// <para>
/// Every change (a capture, a delete, or a clear) increments <see cref="Version"/>. To follow new notifications without
/// re-reading the whole list, read <see cref="Version"/>, call <see cref="WaitForChangeAsync"/> with it, then
/// <see cref="Query"/> with <see cref="PushNotificationCaptureQuery.AfterSequence"/> set to the version you last read.
/// </para>
/// <para>All members are thread-safe.</para>
/// </remarks>
[PublicAPI]
public interface IPushNotificationCaptureStore
{
    /// <summary>Gets the maximum number of notifications the store keeps.</summary>
    int Capacity { get; }

    /// <summary>Gets the number of notifications the store holds now.</summary>
    int Count { get; }

    /// <summary>Gets the change counter, which increases on every capture, delete, and clear.</summary>
    long Version { get; }

    /// <summary>Lists the captured notifications that match <paramref name="query"/>, newest first.</summary>
    /// <param name="query">The filters to apply. Pass <c>new()</c> to list every item.</param>
    /// <returns>A snapshot of the matching notifications.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="PushNotificationCaptureQuery.Limit"/> is zero or negative.</exception>
    IReadOnlyList<CapturedPushNotification> Query(PushNotificationCaptureQuery query);

    /// <summary>Gets the captured notification with the given <see cref="CapturedPushNotification.Id"/>.</summary>
    /// <param name="id">The capture identifier.</param>
    /// <returns>The notification, or <see langword="null"/> when the store does not hold it.</returns>
    CapturedPushNotification? Get(Guid id);

    /// <summary>Removes the captured notification with the given <see cref="CapturedPushNotification.Id"/>.</summary>
    /// <param name="id">The capture identifier.</param>
    /// <returns><see langword="true"/> when the notification was removed; <see langword="false"/> when the store did not hold it.</returns>
    bool Delete(Guid id);

    /// <summary>Removes every captured notification.</summary>
    /// <returns>The number of notifications removed.</returns>
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

/// <summary>Filters a <see cref="IPushNotificationCaptureStore.Query"/> call. Every filter left <see langword="null"/> matches all notifications.</summary>
[PublicAPI]
public sealed record PushNotificationCaptureQuery
{
    /// <summary>
    /// Gets the service instance name to match exactly. An empty string matches the default (unnamed) service.
    /// </summary>
    public string? SenderName { get; init; }

    /// <summary>
    /// Gets text to find, ignoring case, in the title, the body, any client identifier, or any data key or value.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Gets a <see cref="CapturedPushNotification.Sequence"/>; only notifications captured after it match.</summary>
    public long? AfterSequence { get; init; }

    /// <summary>Gets the maximum number of notifications to return. Must be positive.</summary>
    public int? Limit { get; init; }
}
