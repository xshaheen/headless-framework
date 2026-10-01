// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// One assembly's generated Messaging registration: its <see cref="BusConsumerAttribute"/> and
/// <see cref="QueueConsumerAttribute"/> consumers. The Messaging source generator emits one implementation per assembly
/// as <c>&lt;AssemblyName&gt;.MessagingModule</c>.
/// </summary>
/// <remarks>
/// A module adds its generated type explicitly from its own entry point, so an assembly whose module is not added
/// contributes no consumers even when it is loaded. Message contracts are not part of the module: they belong to the
/// message schema and are declared with <c>Message&lt;T&gt;(name, version)</c>.
/// </remarks>
public interface IMessagingModule
{
    /// <summary>
    /// Adds this module's consumers to one host's catalog. The Messaging setup calls it once per host for every added
    /// module; do not call it directly.
    /// </summary>
    /// <param name="catalog">The catalog of the host being built.</param>
    static abstract void Register(MessagingCatalogBuilder catalog);
}
