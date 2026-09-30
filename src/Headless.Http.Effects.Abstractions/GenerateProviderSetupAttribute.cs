// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Http.Effects;

/// <summary>
/// Marks a provider options class as the single source of its provider's registration surface. A source generator
/// (or the hand-written fallback) emits from this class: the <c>Use{Provider}</c> overload trios for the default
/// and named slots, options binding with FluentValidation startup validation, the typed HttpClient whose
/// resilience pipeline is derived from the co-applied <see cref="OutboundEffectAttribute"/>, and the
/// default/keyed sender registrations.
/// </summary>
/// <remarks>
/// The declaration lives on the options class because every templated provider's registration surface is a pure
/// function of its identity (names), its options (binding + validation), its sender constructor (DI shape), and
/// its effect class (resilience). Providers whose setup needs more than that — a custom auth flow, an SDK client
/// that is not a plain DI construct — keep their hand-written <c>Setup.cs</c>; the attribute is simply absent.
/// </remarks>
/// <param name="useMethodName">The <c>Use{Provider}</c> member name, for example <c>UseInfobip</c>.</param>
/// <param name="httpClientName">The named HttpClient the sender sends through, for example <c>Headless:InfobipSms</c>.</param>
/// <param name="senderTypeName">
/// The sender implementation's type name in the options class's namespace (unqualified), for example
/// <c>InfobipSmsSender</c>. Its constructor parameters must follow the provider template:
/// <c>IHttpClientFactory</c>, the <c>httpClientName</c> string, <c>IOptionsMonitor&lt;TOptions&gt;</c>, the
/// <c>optionsName</c> string, then optionally <c>ILogger&lt;TSender&gt;</c> and <c>TimeProvider</c>.
/// </param>
/// <param name="validatorTypeName">
/// The FluentValidation validator's type name in the options class's namespace, for example
/// <c>InfobipSmsOptionsValidator</c>.
/// </param>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GenerateProviderSetupAttribute(
    string useMethodName,
    string httpClientName,
    string senderTypeName,
    string validatorTypeName
) : Attribute
{
    /// <summary>The <c>Use{Provider}</c> member name the generated registration surface exposes.</summary>
    public string UseMethodName { get; } = Argument.IsNotEmpty(useMethodName);

    /// <summary>The named HttpClient the sender sends through.</summary>
    public string HttpClientName { get; } = Argument.IsNotEmpty(httpClientName);

    /// <summary>The sender implementation's type name (unqualified, in the options class's namespace).</summary>
    public string SenderTypeName { get; } = Argument.IsNotEmpty(senderTypeName);

    /// <summary>The FluentValidation validator's type name (unqualified, in the options class's namespace).</summary>
    public string ValidatorTypeName { get; } = Argument.IsNotEmpty(validatorTypeName);
}
