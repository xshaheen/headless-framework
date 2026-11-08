// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Mediator.Behaviors;

/// <summary>Whether the logging behaviors write whole message and response objects into their Debug entries.</summary>
/// <remarks>
/// A message or response carries whatever the caller put in it: passwords, tokens, personal data. Logging it whole is a
/// debugging aid an app opts into, never a default.
/// </remarks>
internal sealed record MediatorPayloadLogging(bool Enabled)
{
    // Any registration that opts in wins, so registering the behaviors twice with different values never turns
    // payloads off behind the caller that asked for them.
    internal static void Register(IServiceCollection services, bool logPayloads)
    {
        if (logPayloads)
        {
            services.Replace(ServiceDescriptor.Singleton(new MediatorPayloadLogging(Enabled: true)));
        }
        else
        {
            services.TryAddSingleton(new MediatorPayloadLogging(Enabled: false));
        }
    }
}
