// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Imaging;

/// <summary>
/// Configures imaging during <c>AddHeadlessImaging(imaging =&gt; …)</c>: the shared <see cref="ImagingOptions" />, and
/// at least one provider chosen through a <c>Use…</c> call such as <c>UseNetVips</c> from
/// <c>Headless.Imaging.NetVips</c>.
/// </summary>
[PublicAPI]
public sealed class HeadlessImagingSetupBuilder
{
    internal HeadlessImagingSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal IList<IImagingProviderOptionsExtension> Extensions { get; } = [];

    /// <summary>Binds <see cref="ImagingOptions" /> from the supplied configuration section.</summary>
    /// <param name="configuration">The configuration section to bind from.</param>
    /// <returns>The same builder so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
    public HeadlessImagingSetupBuilder Configure(IConfiguration configuration)
    {
        Argument.IsNotNull(configuration);

        // Validation is attached once by AddHeadlessImaging, however many Configure calls there are.
        Services.Configure<ImagingOptions>(configuration);

        return this;
    }

    /// <summary>Configures <see cref="ImagingOptions" /> with the supplied delegate.</summary>
    /// <param name="configure">Delegate that mutates the options.</param>
    /// <returns>The same builder so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
    public HeadlessImagingSetupBuilder Configure(Action<ImagingOptions> configure)
    {
        Argument.IsNotNull(configure);

        Services.Configure(configure);

        return this;
    }

    /// <summary>Configures <see cref="ImagingOptions" /> with a delegate that can resolve services.</summary>
    /// <param name="configure">Delegate that mutates the options using the service provider.</param>
    /// <returns>The same builder so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
    public HeadlessImagingSetupBuilder Configure(Action<ImagingOptions, IServiceProvider> configure)
    {
        Argument.IsNotNull(configure);

        Services.Configure(configure);

        return this;
    }

    /// <summary>
    /// Registers a provider extension whose services are added when setup completes. Called by provider packages'
    /// <c>Use…</c> members rather than by application code.
    /// </summary>
    /// <param name="extension">The provider extension.</param>
    /// <exception cref="ArgumentNullException"><paramref name="extension" /> is <see langword="null" />.</exception>
    public void RegisterExtension(IImagingProviderOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }
}
