// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Http.Effects;

/// <summary>
/// Marks the options class of a single-backend HTTP client package (one with no provider choice, so a plain
/// <c>Add{Feature}</c> on <c>IServiceCollection</c> rather than a <c>Use{Provider}</c> slot) as the source of its
/// registration surface. The generator emits the <c>Add{Feature}</c> overload trio, options validation, the named
/// HttpClient, and its resilience pipeline derived from the co-applied <see cref="OutboundEffectAttribute"/>.
/// </summary>
/// <remarks>
/// Service wiring beyond the client — typed brokers, authenticators, token caches — is package-specific, so the
/// generated <c>Setup{Feature}</c> class is partial and requires the package to implement
/// <c>private static partial void AddServices(IServiceCollection services)</c>. A missing implementation is a
/// compile error, not a silently empty registration.
/// </remarks>
/// <param name="addMethodName">The <c>Add{Feature}</c> member name, for example <c>AddPaymobCashOut</c>.</param>
/// <param name="httpClientName">The named HttpClient, for example <c>Headless:PaymobCashOut</c>.</param>
/// <param name="validatorTypeName">
/// The FluentValidation validator's type name, unqualified in the options namespace or fully qualified.
/// </param>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GenerateClientSetupAttribute(string addMethodName, string httpClientName, string validatorTypeName)
    : Attribute
{
    /// <summary>The <c>Add{Feature}</c> member name the generated registration surface exposes.</summary>
    public string AddMethodName { get; } = Argument.IsNotNullOrEmpty(addMethodName);

    /// <summary>The named HttpClient the package's typed client sends through.</summary>
    public string HttpClientName { get; } = Argument.IsNotNullOrEmpty(httpClientName);

    /// <summary>The FluentValidation validator's type name.</summary>
    public string ValidatorTypeName { get; } = Argument.IsNotNullOrEmpty(validatorTypeName);
}
