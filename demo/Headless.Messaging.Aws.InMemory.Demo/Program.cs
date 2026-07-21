using Amazon;
using Headless.Messaging;
using Headless.Messaging.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.Bus.ForConsumersFromAssembly(typeof(Program).Assembly);
    setup.UseInMemoryStorage();
    setup.UseAws(RegionEndpoint.CNNorthWest1);
    setup.UseDashboard(d => d.WithNoAuth());
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.MapControllers();
await app.RunAsync();
