// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination.SqlServer;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SqlServerCoordinationConnectionTests : TestBase
{
    // Membership opens its own connections from a copied connection string, so a context connection that
    // authenticates with an access token would otherwise surface only as a login failure once the host runs.
    [Fact]
    public void coordination_refuses_a_context_connection_that_authenticates_with_an_access_token()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDbContext<TokenApplicationContext>(db =>
            db.UseSqlServer(new SqlConnection("Server=localhost;Database=jobs") { AccessToken = "token" })
        );
        builder.Services.AddHeadlessJobs(jobs =>
        {
            jobs.DisableBackgroundServices();
            jobs.AddModule<CoordinatedJobsModule>();
            jobs.UseSqlServer<TokenApplicationContext>(coordination =>
                coordination.Configure(options => options.ClusterName = "access-token")
            );
        });

        using var host = builder.Build();
        var act = () => host.Services.GetRequiredService<IOptions<SqlServerCoordinationOptions>>().Value;

        act.Should().Throw<InvalidOperationException>().WithMessage("*access token*");
    }

    private sealed class TokenApplicationContext(DbContextOptions<TokenApplicationContext> options)
        : DbContext(options);
}
