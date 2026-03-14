// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Publishes messages through the outbox pattern for reliable, persisted delivery.
/// </summary>
public interface IOutboxPublisher : IMessagePublisher;
