// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting.DependencyInjection;

/// <summary>
/// Accumulates the requirements declared by <c>RequireRegisteredService</c> across every feature that
/// registers into one <c>IServiceCollection</c>, so the startup check can report all of them in a
/// single failure. Registered as a singleton instance during DI setup and mutated afterwards by later
/// <c>Add…</c> calls — the instance the container hands to the startup validator is the same object those
/// calls appended to.
/// </summary>
/// <remarks>
/// Order is preserved (declaration order reads better in the failure message than hash order) while the
/// companion set collapses exact duplicates. The lock guards the theoretical case of a host composing
/// services from more than one thread; contention is nil in practice because registration is a startup
/// activity.
/// </remarks>
internal sealed class RequiredServiceRegistry
{
    private readonly List<RequiredServiceRegistration> _ordered = [];
    private readonly HashSet<RequiredServiceRegistration> _seen = [];
    private readonly Lock _gate = new();

    public void Add(RequiredServiceRegistration registration)
    {
        lock (_gate)
        {
            if (_seen.Add(registration))
            {
                _ordered.Add(registration);
            }
        }
    }

    public IReadOnlyList<RequiredServiceRegistration> Registrations
    {
        get
        {
            lock (_gate)
            {
                return [.. _ordered];
            }
        }
    }
}
