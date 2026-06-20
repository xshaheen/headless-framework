// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

[PublicAPI]
public interface IIntegrationEventEmitter
{
    void AddIntegrationEvent(IIntegrationEvent integrationEvent);

    void ClearIntegrationEvents();

    IReadOnlyList<IIntegrationEvent> GetIntegrationEvents();
}
