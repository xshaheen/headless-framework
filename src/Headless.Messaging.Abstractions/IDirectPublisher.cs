// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Publishes messages directly to the configured transport without persistence.
/// </summary>
public interface IDirectPublisher : IMessagePublisher;
