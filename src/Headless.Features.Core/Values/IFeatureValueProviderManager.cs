// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Models;
using Headless.Features.ValueProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Features.Values;

/// <summary>Provides ordered access to all registered <see cref="IFeatureValueReadProvider"/> instances.</summary>
/// <remarks>
/// Providers are returned in priority order (highest priority first), which is the reverse of their
/// registration order. The default built-in chain is <c>Tenant</c> → <c>Edition</c> → <c>DefaultValue</c>.
/// </remarks>
public interface IFeatureValueProviderManager
{
    /// <summary>Gets the ordered list of value providers, highest priority first.</summary>
    IReadOnlyList<IFeatureValueReadProvider> ValueProviders { get; }
}
