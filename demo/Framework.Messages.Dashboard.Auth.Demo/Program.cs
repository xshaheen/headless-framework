using Demo;

await createHostBuilder(args).Build().RunAsync();

return;

static IHostBuilder createHostBuilder(string[] args)
{
    return Host.CreateDefaultBuilder(args).ConfigureWebHostDefaults(builder => builder.UseStartup<Startup>());
}
