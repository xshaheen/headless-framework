// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Reliability;

/// <summary>
/// Marks a type that a message consumer or job names as its failure policy, so the policy is declared next to the
/// handler it governs and a misspelled policy fails the build instead of a lookup at runtime.
/// </summary>
/// <remarks>
/// A handler names the policy by type in its declaration attribute. The policy a handler runs with resolves in
/// order: the call site, then the handler's declaration, then the host. Until a policy model is supplied for the
/// declared type, a handler that names a policy runs with its host's configured retry behavior.
/// </remarks>
[PublicAPI]
public interface IFailurePolicy;
