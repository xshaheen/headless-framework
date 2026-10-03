// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Headless.Checks;
using Headless.Messaging.Persistence;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Internal;

/// <summary>
/// One additional outbox registered through <c>AddOutbox().Use…()</c>. The provider package supplies the factory;
/// the storage it builds serves only the published rows of its own database.
/// </summary>
internal sealed class OutboxStorageRegistration(string name, Func<IServiceProvider, MessagingOutbox> factory)
{
    public string Name { get; } = Argument.IsNotNullOrWhiteSpace(name);

    public MessagingOutbox Create(IServiceProvider serviceProvider) => factory(serviceProvider);
}
