using Amazon;

var builder = WebApplication.CreateBuilder(args);

builder
    .Services.AddMessages(messaging =>
    {
        messaging.Consumer<AmazonSqsMessageConsumer>().Topic("sample.aws.in-memory").Build();
        messaging.WithTopicMapping<AmazonSqsMessage>("sample.aws.in-memory");
    })
    .AddCap(x =>
    {
        x.UseInMemoryStorage();
        x.UseAmazonSqs(RegionEndpoint.CNNorthWest1);
        x.UseDashboard();
    });

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.MapControllers();
await app.RunAsync();
