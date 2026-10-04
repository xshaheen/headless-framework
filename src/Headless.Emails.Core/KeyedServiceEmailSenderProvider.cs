// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Emails;

/// <summary>
/// Resolves named <see cref="IEmailSender"/> instances from keyed container registrations.
/// </summary>
internal sealed class KeyedServiceEmailSenderProvider(
    IServiceProvider serviceProvider,
    IReadOnlySet<string> registeredNames
) : IEmailSenderProvider
{
    public IReadOnlySet<string> RegisteredNames { get; } = registeredNames;

    public IEmailSender GetSender(string name)
    {
        Argument.IsNotNullOrWhiteSpace(name);

        return serviceProvider.GetKeyedService<IEmailSender>(name)
            ?? throw new InvalidOperationException(
                $"No email sender is registered under the name '{name}'. Register a named instance first — for "
                    + $"example setup.AddNamed(\"{name}\", i => i.UseAwsSes(…)), i.UseAzure(…), i.UseMailkit(…), "
                    + "i.UseDevelopment(…), or i.UseNoop()."
            );
    }

    public IEmailSender? GetSenderOrNull(string name)
    {
        Argument.IsNotNullOrWhiteSpace(name);

        return serviceProvider.GetKeyedService<IEmailSender>(name);
    }
}
