// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Emails;

/// <summary>
/// <see cref="IEmailSenderProvider"/> over the container's keyed <see cref="IEmailSender"/> registrations —
/// resolves the named instances added through <c>setup.AddNamed(name, …)</c>.
/// </summary>
internal sealed class KeyedServiceEmailSenderProvider(IServiceProvider serviceProvider) : IEmailSenderProvider
{
    public IEmailSender GetSender(string name)
    {
        Argument.IsNotNullOrEmpty(name);

        return serviceProvider.GetKeyedService<IEmailSender>(name)
            ?? throw new InvalidOperationException(
                $"No email sender is registered under the name '{name}'. Register a named instance first — for "
                    + $"example setup.AddNamed(\"{name}\", i => i.UseAwsSes(…)), i.UseAzure(…), i.UseMailkit(…), "
                    + "i.UseDevelopment(…), or i.UseNoop()."
            );
    }

    public IEmailSender? GetSenderOrNull(string name)
    {
        Argument.IsNotNullOrEmpty(name);

        return serviceProvider.GetKeyedService<IEmailSender>(name);
    }
}
