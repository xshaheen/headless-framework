/*
private void InitializeDynamicPermissions(ApplicationInitializationContext context)
{
    var options = context.ServiceProvider.GetRequiredService<IOptions<PermissionManagementOptions>>().Value;

    if (!options.SaveStaticPermissionsToDatabase && !options.IsDynamicPermissionStoreEnabled)
    {
        return;
    }

    var rootServiceProvider = context.ServiceProvider.GetRequiredService<IRootServiceProvider>();

    _initializeDynamicPermissionsTask = Task.Run(async () =>
    {
        using var scope = rootServiceProvider.CreateScope();
        var applicationLifetime = scope.ServiceProvider.GetService<IHostApplicationLifetime>();
        var cancellationTokenProvider = scope.ServiceProvider.GetRequiredService<ICancellationTokenProvider>();
        var cancellationToken = applicationLifetime?.ApplicationStopping ?? _cancellationTokenSource.Token;

        try
        {
            using (cancellationTokenProvider.Use(cancellationToken))
            {
                if (cancellationTokenProvider.Token.IsCancellationRequested)
                {
                    return;
                }

                await SaveStaticPermissionsToDatabaseAsync(options, scope, cancellationTokenProvider);

                if (cancellationTokenProvider.Token.IsCancellationRequested)
                {
                    return;
                }

                await PreCacheDynamicPermissionsAsync(options, scope);
            }
        }
        // ReSharper disable once EmptyGeneralCatchClause (No need to log since it is logged above)
        catch { }
    });
}

private static async Task SaveStaticPermissionsToDatabaseAsync(
    PermissionManagementOptions options,
    IServiceScope scope,
    ICancellationTokenProvider cancellationTokenProvider
)
{
    if (!options.SaveStaticPermissionsToDatabase)
    {
        return;
    }

    await Policy
        .Handle<Exception>()
        .WaitAndRetryAsync(
            8,
            retryAttempt =>
                TimeSpan.FromSeconds(
                    RandomHelper.GetRandom((int)Math.Pow(2, retryAttempt) * 8, (int)Math.Pow(2, retryAttempt) * 12)
                )
        )
        .ExecuteAsync(
            async _ =>
            {
                try
                {
                    // ReSharper disable once AccessToDisposedClosure
                    await scope.ServiceProvider.GetRequiredService<IStaticPermissionSaver>().SaveAsync();
                }
                catch (Exception ex)
                {
                    // ReSharper disable once AccessToDisposedClosure
                    scope
                        .ServiceProvider.GetService<ILogger<AbpPermissionManagementDomainModule>>()
                        ?.LogException(ex);

                    throw; // Polly will catch it
                }
            },
            cancellationTokenProvider.Token
        );
}

private static async Task PreCacheDynamicPermissionsAsync(PermissionManagementOptions options, IServiceScope scope)
{
    if (!options.IsDynamicPermissionStoreEnabled)
    {
        return;
    }

    try
    {
        // Pre-cache permissions, so first request doesn't wait
        await scope.ServiceProvider.GetRequiredService<IDynamicPermissionDefinitionStore>().GetGroupsAsync();
    }
    catch (Exception ex)
    {
        // ReSharper disable once AccessToDisposedClosure
        scope.ServiceProvider.GetService<ILogger<AbpPermissionManagementDomainModule>>()?.LogException(ex);

        throw; // It will be cached in InitializeDynamicPermissions
    }
}
*/
