// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Headless.EntityFramework;

/// <summary>
/// The context that opened a private DI scope for itself, recorded in that scope so the context's own registration
/// resolves to it there.
/// </summary>
/// <remarks>
/// A context created outside any scope (the pooled <c>IDbContextFactory</c>, or <see langword="new"/> over root
/// options) runs its save in a private scope, and the domain-event handlers that save dispatches are resolved from
/// that scope. A handler that injects the context type would otherwise get another instance, with its own connection
/// and no unit of work, so <c>db.UnitOfWork()</c> would return <see langword="null"/> and an enlisted publish would be
/// skipped. The scope exists only for this context, so its context is this context, as the non-pooled factory's
/// scope resolves the context it created.
/// </remarks>
internal sealed class HeadlessPrivateScopeOwner
{
    public DbContext? Context { get; set; }
}
