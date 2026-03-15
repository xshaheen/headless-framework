using Headless.Abstractions;
using Headless.Testing.Helpers;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Fixture;

/// <summary>
/// EF options extension that registers test doubles into EF's internal service provider.
///
/// When <c>HeadlessDbContext.this.GetService&lt;T&gt;()</c> is called, it resolves from EF's
/// internal service provider (populated by <c>ApplyServices</c> on each
/// <c>IDbContextOptionsExtension</c>), NOT from the application DI scope. This extension injects
/// the test doubles so that <c>IClock</c>, <c>ICurrentUser</c>, <c>ICurrentTenant</c> etc. in
/// <c>HeadlessDbContext._CaptureAuditEntries</c> resolve to our controlled implementations.
/// </summary>
internal sealed class TestHeadlessServicesOptionsExtension(
    TestClock clock,
    TestCurrentUser currentUser,
    TestCurrentTenant currentTenant
) : IDbContextOptionsExtension
{
    public void ApplyServices(IServiceCollection services)
    {
        // Override the defaults registered by AddHeadlessDbContextServices()
        // (which are added by HeadlessDbContextOptionsExtension.ApplyServices).
        // Register as concrete type AND as interface so EF's provider resolves correctly.
        services.AddSingleton<TimeProvider>(clock.TimeProvider);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<ICurrentUser>(currentUser);
        services.AddSingleton<ICurrentTenant>(currentTenant);
    }

    public void Validate(IDbContextOptions options) { }

    public DbContextOptionsExtensionInfo Info => new TestExtensionInfo(this);

    private sealed class TestExtensionInfo(IDbContextOptionsExtension e)
        : DbContextOptionsExtensionInfo(e)
    {
        public override string LogFragment => "TestHeadlessServicesExtension";
        public override bool IsDatabaseProvider => false;
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) { }
        public override int GetServiceProviderHashCode() => 0;
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is TestExtensionInfo;
    }
}
