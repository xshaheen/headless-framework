using Amazon;
using Headless.Dashboard;
using Headless.Messaging;
using Headless.Messaging.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.AddModule<Headless.Messaging.Aws.InMemory.Demo.MessagingModule>();
    setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
    setup.UseInMemoryStorage();
    setup.UseAws(RegionEndpoint.CNNorthWest1);
});

// The Messaging module of the Headless dashboard, at /headless/messaging. No auth: a local demo only.
builder.Services.AddHeadlessDashboard(dashboard => dashboard.WithNoAuth().AddMessaging());

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.MapControllers();
app.MapHeadlessDashboard();

await app.RunAsync();
