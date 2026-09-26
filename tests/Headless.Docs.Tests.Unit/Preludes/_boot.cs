// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Compiled only with examples marked `<!-- example: boot -->`. Their statements run against the builder created here,
// then the host starts and stops, so registration and startup validation failures surface as exceptions.

public static class DocsBoot
{
    private static readonly TimeSpan _Timeout = TimeSpan.FromSeconds(30);

    // Some subsystems validate from a BackgroundService after StartAsync returns (Messaging's bootstrapper), so the
    // host stays up this long for a failure to surface before it is stopped.
    private static readonly TimeSpan _SettleTime = TimeSpan.FromSeconds(2);

    public static WebApplicationBuilder CreateBuilder()
    {
        // Development turns on DI scope and build-time validation, the strictest setting a consumer runs with.
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Development }
        );

        // Port 0 lets parallel boots bind without colliding.
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        return builder;
    }

    public static async Task RunAsync(WebApplicationBuilder builder)
    {
        using var timeout = new CancellationTokenSource(_Timeout);
        await using var app = builder.Build();

        var backgroundServices = app.Services.GetServices<IHostedService>().OfType<BackgroundService>().ToList();

        try
        {
            await app.StartAsync(timeout.Token);

            var settled = DateTime.UtcNow + _SettleTime;

            while (
                DateTime.UtcNow < settled
                && !app.Lifetime.ApplicationStopping.IsCancellationRequested
                && _Faulted(backgroundServices) is null
            )
            {
                await Task.Delay(50, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (_Faulted(backgroundServices) is not null) { }

        // A failed BackgroundService stops the host instead of throwing from StartAsync; report its exception.
        if (_Faulted(backgroundServices) is { } fault)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(fault);
        }

        await app.StopAsync(timeout.Token);
    }

    private static Exception? _Faulted(IEnumerable<BackgroundService> services)
    {
        return services
            .Select(static s => s.ExecuteTask)
            .FirstOrDefault(static t => t is { IsFaulted: true })
            ?.Exception?.GetBaseException();
    }
}
