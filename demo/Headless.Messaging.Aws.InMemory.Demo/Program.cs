using Amazon;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Messaging.Dashboard;
using Headless.Messaging.InMemoryStorage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.ForMessagesFromAssembly(typeof(Program).Assembly);
    setup.UseInMemoryStorage();
    setup.UseAws(RegionEndpoint.CNNorthWest1);
    setup.UseDashboard(d => d.WithNoAuth());
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.MapControllers();
await app.RunAsync();
