// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Transport;

/// <summary>
/// A capability of a provider's per-contract config: headers the publish path computes from each payload and stamps on
/// the message before it is stored, such as a partition key or a subject token.
/// </summary>
/// <remarks>
/// <para>
/// The core discovers it by a type check on each config attached through
/// <see cref="IMessageProviderConfigBuilder{TMessage}"/>; a config that does not implement it contributes nothing. A
/// contribution whose selector returns <see langword="null"/> or white space adds no header, and a selector that throws
/// fails the publish.
/// </para>
/// <para>
/// <b>Evolution policy:</b> providers implement it. It never gains a member; a new publish-time capability arrives as another
/// interface the config can also implement.
/// </para>
/// </remarks>
public interface IProviderHeaderContributions
{
    /// <summary>Gets the headers to compute, in the order they are applied.</summary>
    IReadOnlyList<ProviderHeaderContribution> HeaderContributions { get; }
}

/// <summary>
/// A capability of a provider's per-contract config that must refuse a publish without a payload, because a value it
/// needs is computed from the payload and the message would otherwise be stored with no way to send it.
/// </summary>
/// <remarks>
/// The core discovers it by a type check on each attached config. <b>Evolution policy:</b> providers implement it, and it never
/// gains a member; a new publish-time capability arrives as another interface.
/// </remarks>
public interface IProviderMissingPayloadValidation
{
    /// <summary>Called when a message of the config's contract is published with a <see langword="null"/> payload.</summary>
    /// <param name="messageName">The resolved message name.</param>
    /// <param name="lane">The lane the message is published on.</param>
    /// <exception cref="InvalidOperationException">The contract lane cannot be published without a payload.</exception>
    void ValidateMissingPayload(string messageName, MessageLane lane);
}

/// <summary>One header a provider config computes from a payload.</summary>
/// <param name="HeaderName">The header to set; it must not be a header the core reserves.</param>
/// <param name="Selector">Reads the header value from the payload; <see langword="null"/> adds no header.</param>
/// <remarks>The pair never gains a member.</remarks>
public readonly record struct ProviderHeaderContribution(string HeaderName, Func<object, string?> Selector)
{
    /// <summary>Gets the header to set.</summary>
    public string HeaderName { get; init; } = Argument.IsNotNullOrWhiteSpace(HeaderName);

    /// <summary>Gets the selector that reads the header value from the payload.</summary>
    public Func<object, string?> Selector { get; init; } = Argument.IsNotNull(Selector);
}
