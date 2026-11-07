// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>
/// Hears every state write the publish and consume pipelines made to a stored row, after the write returned, so a
/// same-process reader such as the dashboard can show the change without waiting for its next poll of the store.
/// </summary>
/// <remarks>
/// <para>
/// Only writes that landed and stand on their own are reported: a sender's or a consumer's state change of a row it
/// holds. A row written inside an application transaction is not reported, because it may not have committed yet; a
/// change read of the store finds it instead.
/// </para>
/// <para>
/// An observer runs on the delivery path, so it must return at once and never throw: queue the work and do it
/// elsewhere.
/// </para>
/// </remarks>
internal interface IMessageStateObserver
{
    /// <summary>Called after <paramref name="message"/>'s state write landed in its store.</summary>
    /// <param name="messageType">The table the row lives in.</param>
    /// <param name="message">The row as the pipeline holds it after the write.</param>
    void OnStateWritten(MessageType messageType, MediumMessage message);
}
