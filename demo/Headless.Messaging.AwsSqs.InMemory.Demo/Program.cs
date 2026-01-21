using Amazon;
using Headless.Messaging.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMessages(x =>
{
    x.ScanConsumers(typeof(Program).Assembly);
    x.UseInMemoryStorage();
    x.UseAmazonSqs(RegionEndpoint.CNNorthWest1);
    x.UseDashboard();
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.MapControllers();
await app.RunAsync();
