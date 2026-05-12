// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Headless.MultiTenancy;

/// <summary>Root builder used by Headless packages to contribute tenant posture from their own package boundaries.</summary>
public sealed class HeadlessTenancyBuilder
{
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
    /// <param name="seam">The seam name, such as <c>Http</c>, <c>Mediator</c>, <c>Messaging</c>, or <c>EntityFramework</c>.</param>
    /// <param name="status">The configured posture status for the seam.</param>
    /// <param name="capabilities">Optional capability labels exposed by the seam.</param>
    /// <returns>The same root builder.</returns>
    public HeadlessTenancyBuilder RecordSeam(string seam, TenantPostureStatus status, params string[] capabilities)
    {
        Manifest.RecordSeam(seam, status, capabilities);
        return this;
    }
}
