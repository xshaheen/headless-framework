// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.DataProtection;
using Headless.Blobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>A test host with a default and a named FileSystem store behind the signed-URL endpoint.</summary>
internal sealed class SignedUrlTestApp : IAsyncDisposable
{
    public const string Container = "reports";
    public const string NamedStore = "docs";

    private readonly string _root;

    private SignedUrlTestApp(WebApplication app, FakeTimeProvider time, string root)
    {
        App = app;
        Time = time;
        _root = root;
    }

    public WebApplication App { get; }

    public FakeTimeProvider Time { get; }

    public IBlobStorage DefaultStorage => App.Services.GetRequiredService<IBlobStorage>();

    public IBlobStorage NamedStorage => App.Services.GetRequiredKeyedService<IBlobStorage>(NamedStore);

    /// <summary>Starts the test host with both stores' containers provisioned.</summary>
    /// <param name="cancellationToken">Cancels startup and container provisioning.</param>
    /// <param name="persistKeysToBlobStorage">
    /// Persist the data-protection key ring to the default store instead of using an ephemeral provider, the pairing
    /// the Blobs guide recommends for multi-replica hosts.
    /// </param>
    public static async Task<SignedUrlTestApp> StartAsync(
        CancellationToken cancellationToken,
        bool persistKeysToBlobStorage = false
    )
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-26T10:00:00Z", CultureInfo.InvariantCulture));
        var root = Path.Combine(Path.GetTempPath(), "headless-signed-url-" + Guid.NewGuid().ToString("N"));

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<TimeProvider>(time);

        if (persistKeysToBlobStorage)
        {
            builder.Services.AddDataProtection().PersistKeysToBlobStorage();
        }
        else
        {
            builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        }

        builder.Services.AddHeadlessBlobs(setup =>
        {
            setup.UseFileSystem(options => options.BaseDirectoryPath = Path.Combine(root, "default"));
            setup.AddNamed(
                NamedStore,
                instance => instance.UseFileSystem(options => options.BaseDirectoryPath = Path.Combine(root, "named"))
            );
            setup.UseSignedUrlEndpoint(options => options.BaseUrl = new Uri("http://localhost"));
        });

        var app = builder.Build();
        app.MapBlobSignedUrlEndpoint();
        await app.StartAsync(cancellationToken);

        var testApp = new SignedUrlTestApp(app, time, root);

        await app
            .Services.GetRequiredService<IBlobContainerManager>()
            .EnsureContainerAsync(Container, cancellationToken);
        await app
            .Services.GetRequiredKeyedService<IBlobContainerManager>(NamedStore)
            .EnsureContainerAsync(Container, cancellationToken);

        return testApp;
    }

    public HttpClient CreateClient()
    {
        return App.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
