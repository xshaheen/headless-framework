// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Orm.EntityFramework.GlobalFilters;

/// <inheritdoc />
public sealed class HeadlessDbContextOptionsExtension : IDbContextOptionsExtension
{
    public void ApplyServices(IServiceCollection services) => services.AddHeadlessDbContextServices();

    public void Validate(IDbContextOptions options) { }

    public DbContextOptionsExtensionInfo Info => new HeadlessOptionsExtensionInfo(this);

    private sealed class HeadlessOptionsExtensionInfo(IDbContextOptionsExtension e) : DbContextOptionsExtensionInfo(e)
    {
        public override string LogFragment => "HeadlessOptionsExtension";

        public override bool IsDatabaseProvider => false;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) { }

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        {
            return other is HeadlessOptionsExtensionInfo;
        }
    }
}
