// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework;

/// <summary>
/// EF Core options extension that registers Headless EF Core infrastructure services via the
/// <c>IDbContextOptionsExtension</c> hook. Attached by <see cref="SetupOptionsExtension.AddHeadlessExtension(DbContextOptionsBuilder)"/>.
/// </summary>
[PublicAPI]
public sealed class HeadlessDbContextOptionsExtension : IDbContextOptionsExtension
{
    /// <summary>
    /// Called by EF Core when building the internal service provider; delegates to
    /// <c>AddHeadlessDbContextServices()</c>.
    /// </summary>
    /// <param name="services">The EF Core internal service collection.</param>
    public void ApplyServices(IServiceCollection services)
    {
        services.AddHeadlessDbContextServices();
    }

    /// <summary>Performs no validation; all Headless prerequisites are validated at startup by DI.</summary>
    /// <param name="options">The current EF Core options.</param>
    public void Validate(IDbContextOptions options) { }

    /// <summary>Extension metadata used by EF Core for logging and service-provider hashing.</summary>
    public DbContextOptionsExtensionInfo Info => new HeadlessOptionsExtensionInfo(this);

    private sealed class HeadlessOptionsExtensionInfo(IDbContextOptionsExtension e) : DbContextOptionsExtensionInfo(e)
    {
        public override string LogFragment => "HeadlessOptionsExtension";

        public override bool IsDatabaseProvider => false;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) { }

        public override int GetServiceProviderHashCode()
        {
            return 0;
        }

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        {
            return other is HeadlessOptionsExtensionInfo;
        }
    }
}
