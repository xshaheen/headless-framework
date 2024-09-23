// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

// ReSharper disable once CheckNamespace
namespace Framework.Messaging;

public interface IMessageBus : IMessagePublisher, IMessageSubscriber, IDisposable;
