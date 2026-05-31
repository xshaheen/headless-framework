using Amazon;
using Headless.Messaging.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ForMessagesFromAssembly(typeof(Program).Assembly);

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UseInMemoryStorage();
    setup.UseAws(RegionEndpoint.CNNorthWest1);
    setup.UseDashboard(d => d.WithNoAuth());
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.MapControllers();
await app.RunAsync();
