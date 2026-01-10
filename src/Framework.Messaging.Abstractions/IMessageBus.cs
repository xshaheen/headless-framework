// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Messaging;

public interface IMessageBus : IMessagePublisher, IMessageSubscriber, IAsyncDisposable;
