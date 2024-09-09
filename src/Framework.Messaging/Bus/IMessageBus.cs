// ReSharper disable once CheckNamespace
namespace Framework.Messaging;

public interface IMessageBus : IMessagePublisher, IMessageSubscriber, IDisposable;
