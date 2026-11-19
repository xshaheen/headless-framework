// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Coordination;

/// <summary>
/// Registers a test membership store the way a custom store package does, so <c>AddHeadlessCoordination</c> adds the
/// heartbeat and its stop ordering exactly as it does for a shipped provider.
/// </summary>
internal sealed class MembershipStoreProviderExtension<TStore> : ICoordinationProviderOptionsExtension
    where TStore : class, IMembershipStore
{
    public void AddServices(IServiceCollection services)
    {
        services.AddCoordinationCore<TStore>();
    }
}
