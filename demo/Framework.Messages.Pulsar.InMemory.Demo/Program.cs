using Framework.Messages;

var builder = WebApplication.CreateBuilder(args);

var pulsarUri = builder.Configuration.GetValue("AppSettings:PulsarUri", "pulsar://localhost:6650");

builder.Services.AddMessages(messaging =>
{
    messaging.ScanConsumers(typeof(Program).Assembly);
});

builder.Services.AddCap(x =>
{
    x.UseInMemoryStorage();
    x.UsePulsar(pulsarUri);
    x.UseDashboard();
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.MapControllers();
await app.RunAsync();
