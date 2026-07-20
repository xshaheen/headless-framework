// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Headless.MultiTenancy;

/// <summary>Root builder used by Headless packages to contribute tenant posture from their own package boundaries.</summary>
[PublicAPI]
public sealed class HeadlessTenancyBuilder
{
    /// <summary>Initializes the root builder for a host and its shared posture manifest.</summary>
    /// <param name="applicationBuilder">The host application builder being configured.</param>
    /// <param name="manifest">The shared tenant posture manifest for this host.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="applicationBuilder"/> or <paramref name="manifest"/> is <see langword="null"/>.
    /// </exception>
    internal HeadlessTenancyBuilder(IHostApplicationBuilder applicationBuilder, TenantPostureManifest manifest)
    {
        ApplicationBuilder = Argument.IsNotNull(applicationBuilder);
        Manifest = Argument.IsNotNull(manifest);
    }

    /// <summary>The host application builder being configured.</summary>
    public IHostApplicationBuilder ApplicationBuilder { get; }

    /// <summary>The service collection being configured.</summary>
    public IServiceCollection Services => ApplicationBuilder.Services;

    /// <summary>The shared tenant posture manifest for this host.</summary>
    public TenantPostureManifest Manifest { get; }

    /// <summary>Records that a Headless package configured a tenant-aware seam.</summary>
    /// <param name="seam">
    /// The seam name, such as <c>Http</c>, <c>Authorization</c>, <c>Messaging</c>, or <c>EntityFramework</c>.
    /// </param>
    /// <param name="status">The configured posture status for the seam.</param>
    /// <param name="capabilities">
    /// Optional non-PII capability labels exposed by the seam. Null or whitespace-only labels are
    /// silently ignored; the array itself must not be <see langword="null"/>.
    /// </param>
    /// <returns>The same root builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="seam"/> or <paramref name="capabilities"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="seam"/> is empty or white space.</exception>
    /// <exception cref="System.ComponentModel.InvalidEnumArgumentException">
    /// <paramref name="status"/> is not a defined <see cref="TenantPostureStatus"/> value (validated when
    /// the seam already has a recorded posture and the two are merged).
    /// </exception>
    public HeadlessTenancyBuilder RecordSeam(string seam, TenantPostureStatus status, params string[] capabilities)
    {
        Manifest.RecordSeam(seam, status, capabilities);
        return this;
    }
}
