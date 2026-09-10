// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting.DependencyInjection;

/// <summary>
/// One "the host must register this service" prerequisite declared during DI setup by a package that
/// consumes a contract it does not itself implement — typically an abstractions-only package whose
/// implementation ships in a separate provider package the host chooses.
/// </summary>
/// <param name="ServiceType">The service contract that must be resolvable from the built container.</param>
/// <param name="RequiredBy">
/// User-facing name of the feature that needs it (for example <c>"Headless settings value caching"</c>).
/// Names the capability an operator recognises, not the internal class that injects the contract.
/// </param>
/// <param name="Remedy">The concrete call that satisfies the requirement, quoted verbatim in the failure message.</param>
/// <remarks>
/// Value equality is what makes repeated identical declarations idempotent: two packages (or one package
/// registered twice) asking for the same contract with the same wording collapse into a single entry
/// instead of repeating a line in the startup failure.
/// </remarks>
[PublicAPI]
public sealed record RequiredServiceRegistration(Type ServiceType, string RequiredBy, string Remedy);
