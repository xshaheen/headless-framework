using Headless.Abstractions;
using Headless.Testing.Helpers;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Fixture;

/// <summary>
/// <para>EF options extension that registers test doubles into EF's internal service provider.</para>
/// <para>
/// When <c>HeadlessDbContext.this.GetService&lt;T&gt;()</c> is called, it resolves from EF's
/// internal service provider (populated by <c>ApplyServices</c> on each
/// <c>IDbContextOptionsExtension</c>), NOT from the application DI scope. This extension injects
/// the test doubles so that <c>TimeProvider</c>, <c>ICurrentUser</c>, <c>ICurrentTenant</c> etc.
/// resolve to our controlled implementations.
/// </para>
/// </summary>
internal sealed class TestHeadlessServicesOptionsExtension(
    FakeTimeProvider timeProvider,
    TestCurrentUser currentUser,
    TestCurrentTenant currentTenant
) : IDbContextOptionsExtension
{
    public void ApplyServices(IServiceCollection services)
    {
        // Override the defaults registered by AddHeadlessDbContextServices()
        // (which are added by HeadlessDbContextOptionsExtension.ApplyServices).
        // Register as concrete type AND as base type so EF's provider resolves correctly.
        services.AddSingleton(timeProvider);
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton<ICurrentUser>(currentUser);
        services.AddSingleton<ICurrentTenant>(currentTenant);
    }

    public void Validate(IDbContextOptions options) { }

    public DbContextOptionsExtensionInfo Info => new TestExtensionInfo(this);

    private sealed class TestExtensionInfo(IDbContextOptionsExtension e) : DbContextOptionsExtensionInfo(e)
    {
        public override string LogFragment => "TestHeadlessServicesExtension";
        public override bool IsDatabaseProvider => false;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) { }

        public override int GetServiceProviderHashCode()
        {
            return 0;
        }

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        {
            return other is TestExtensionInfo;
        }
    }
}
