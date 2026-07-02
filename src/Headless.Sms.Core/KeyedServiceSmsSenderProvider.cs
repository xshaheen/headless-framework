// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Sms;

/// <summary>
/// <see cref="ISmsSenderProvider"/> over the container's keyed <see cref="ISmsSender"/> registrations —
/// resolves the named instances added through <c>setup.AddNamed(name, …)</c>.
/// </summary>
internal sealed class KeyedServiceSmsSenderProvider(
    IServiceProvider serviceProvider,
    IReadOnlySet<string> registeredNames
) : ISmsSenderProvider
{
    public IReadOnlySet<string> RegisteredNames { get; } = registeredNames;

    public ISmsSender GetSender(string name)
    {
        Argument.IsNotNullOrWhiteSpace(name);

        return serviceProvider.GetKeyedService<ISmsSender>(name)
            ?? throw new InvalidOperationException(
                $"No SMS sender is registered under the name '{name}'. Register a named instance first — for "
                    + $"example setup.AddNamed(\"{name}\", i => i.UseTwilio(…)), i.UseAwsSns(…), i.UseCequens(…), "
                    + "i.UseConnekio(…), i.UseInfobip(…), i.UseVictoryLink(…), i.UseVodafone(…), i.UseDevelopment(…), "
                    + "or i.UseNoop()."
            );
    }

    public ISmsSender? GetSenderOrNull(string name)
    {
        Argument.IsNotNullOrWhiteSpace(name);

        return serviceProvider.GetKeyedService<ISmsSender>(name);
    }
}
