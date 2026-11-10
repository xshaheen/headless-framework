// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Headless.Mediator.Behaviors;

/// <summary>Whether the logging behaviors write whole message and response objects into their Debug entries.</summary>
/// <remarks>
/// A message or response carries whatever the caller put in it: passwords, tokens, personal data. Logging it whole is a
/// debugging aid, so it is on only when the host runs in the Development environment. A host with no
/// <see cref="IHostEnvironment"/> registered counts as Production.
/// </remarks>
internal sealed record MediatorPayloadLogging(bool Enabled)
{
    internal static void Register(IServiceCollection services)
    {
        services.TryAddSingleton(static provider => new MediatorPayloadLogging(
            provider.GetService<IHostEnvironment>()?.IsDevelopment() == true
        ));
    }
}
