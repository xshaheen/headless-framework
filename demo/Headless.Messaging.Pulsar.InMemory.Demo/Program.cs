using Headless.Messaging.Dashboard;

var builder = WebApplication.CreateBuilder(args);

var pulsarUri = builder.Configuration.GetValue("AppSettings:PulsarUri", "pulsar://localhost:6650");

builder.Services.AddHeadlessMessaging(x =>
{
    x.SubscribeFromAssembly(typeof(Program).Assembly);
    x.UseInMemoryStorage();
    x.UsePulsar(pulsarUri);
    x.UseDashboard(d => d.WithNoAuth());
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.MapControllers();
await app.RunAsync();
