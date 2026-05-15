using Amazon;
using Headless.Messaging.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.SubscribeFromAssembly(typeof(Program).Assembly);
    setup.UseInMemoryStorage();
    setup.UseAmazonSqs(RegionEndpoint.CNNorthWest1);
    setup.UseDashboard(d => d.WithNoAuth());
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.MapControllers();
await app.RunAsync();
