using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Dashboard;

var builder = WebApplication.CreateBuilder(args);

var pulsarUri = builder.Configuration.GetValue("AppSettings:PulsarUri", "pulsar://localhost:6650");

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.Bus.ForConsumersFromAssembly(
        typeof(Program).Assembly,
        static (_, consumer) => consumer.ConsumerIdentity("pulsar-demo.message")
    );
    setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
    setup.UseInMemoryStorage();
    setup.UsePulsar(pulsarUri);
    setup.UseDashboard(d => d.WithNoAuth());
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.MapControllers();
await app.RunAsync();
