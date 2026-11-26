// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SqlServerPermissionsEntityFrameworkModelTests : TestBase
{
    [Fact]
    public void should_emit_binary_collation_for_permission_tenant_ids()
    {
        using var services = new ServiceCollection().AddOptions().BuildServiceProvider();
        var options = new DbContextOptionsBuilder<PermissionsDbContext>()
            .UseSqlServer("Server=localhost;Database=PermissionsModel")
            .UseApplicationServiceProvider(services)
            .Options;
        using var context = new PermissionsDbContext(options);

        var script = context.Database.GenerateCreateScript();

        script.Should().Contain("[TenantId] nvarchar(41) COLLATE Latin1_General_100_BIN2 NULL");
    }

    private sealed class PermissionsDbContext(DbContextOptions<PermissionsDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessPermissions(this);
        }
    }
}
