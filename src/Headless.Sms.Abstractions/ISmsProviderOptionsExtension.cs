// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Sms;

/// <summary>
/// Setup-time extension hook contributed by an SMS provider package (Twilio, AWS SNS, Cequens, ...).
/// </summary>
/// <remarks>
/// A provider's <c>Use{Provider}</c> builder method registers one implementation of this interface on the
/// <see cref="HeadlessSmsSetupBuilder"/>; <see cref="AddServices"/> runs later from <c>AddHeadlessSms</c>,
/// after the exactly-one-provider gate has passed.
/// </remarks>
[PublicAPI]
public interface ISmsProviderOptionsExtension
{
    /// <summary>Registers the provider's services into the container.</summary>
    void AddServices(IServiceCollection services);
}
