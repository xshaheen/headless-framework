// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Guarantees;

/// <summary>
/// Marks a test as evidence for a catalogued messaging guarantee, so the catalogue policy can prove every
/// guarantee is exercised by a test that runs.
/// </summary>
/// <remarks>
/// Put the attribute on the shared harness method when the guarantee lives in a contract suite: a provider leaf that
/// overrides the method inherits it, and the provider's evidence test fails when the leaf does not run it.
/// </remarks>
/// <param name="id">The catalogue ID, such as <c>MSG-INBOX-01</c>.</param>
[PublicAPI]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class GuaranteeAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}
