using Headless.Messaging.Dashboard;

var builder = WebApplication.CreateBuilder(args);

var pulsarUri = builder.Configuration.GetValue("AppSettings:PulsarUri", "pulsar://localhost:6650");

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.SubscribeFromAssembly(typeof(Program).Assembly);
    setup.UseInMemoryStorage();
    setup.UsePulsar(pulsarUri);
    setup.UseDashboard(d => d.WithNoAuth());
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.MapControllers();
await app.RunAsync();
