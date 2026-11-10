// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Dashboard;

/// <summary>Registers and maps how the dashboard serves its document and build files.</summary>
internal static class DashboardServing
{
    private static readonly string[] _ReadMethods = [HttpMethods.Get, HttpMethods.Head];

    /// <summary>
    /// Registers the shell's build, the served build files, the document, and the session reader.
    /// </summary>
    public static void AddServingServices(IServiceCollection services)
    {
        services.TryAddSingleton(static _ => DashboardShellAssets.CreateEmbedded());
        services.TryAddSingleton<DashboardAssetCatalog>();
        services.TryAddSingleton<ShellDocument>();
        services.TryAddSingleton<IDashboardSessionSnapshot, PermissionsDashboardSessionSnapshot>();
    }

    /// <summary>
    /// Maps the document and build-file routes on the dashboard's route group. Every route is anonymous, because the
    /// sign-in page needs the shell before anyone is signed in; the document carries only what the session allows.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><c>assets/{**path}</c>: the shell's build files.</description></item>
    /// <item>
    /// <description>
    /// <c>m/{id}/{**path}</c>: a loaded module's build files; a <c>404</c> for an unknown or refused module and for its
    /// <c>manifest.json</c>, which the shell reads itself.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>api/{**path}</c>: a <c>404</c> for any API path no other endpoint matched, so a mistyped API call never gets
    /// the HTML document.
    /// </description>
    /// </item>
    /// <item>
    /// <description><c>{**path}</c>: the document, for the base path, <c>sign-in</c>, and every deep link.</description>
    /// </item>
    /// </list>
    /// </remarks>
    public static void MapServingEndpoints(RouteGroupBuilder group)
    {
        group
            .MapMethods(
                "/assets/{**path}",
                _ReadMethods,
                static (HttpContext context, string? path) =>
                    EmbeddedAssetEndpoints.ServeAsync(
                        context,
                        context.RequestServices.GetRequiredService<DashboardAssetCatalog>().Shell,
                        ShellTemplate.AssetsFolder + path
                    )
            )
            .AllowAnonymous()
            .ExcludeFromDescription();

        group
            .MapMethods(
                "/m/{id}/{**path}",
                _ReadMethods,
                static (HttpContext context, string id, string? path) => _ServeModuleAsync(context, id, path)
            )
            .AllowAnonymous()
            .ExcludeFromDescription();

#pragma warning disable ASP0018 // False positive: these catch-alls only claim every path under them; no handler needs the matched rest.
        group.Map("/api/{**path}", static () => Results.NotFound()).AllowAnonymous().ExcludeFromDescription();

        group
            .MapMethods(
                "/{**path}",
                _ReadMethods,
                static context => context.RequestServices.GetRequiredService<ShellDocument>().WriteAsync(context)
            )
            .AllowAnonymous()
            .ExcludeFromDescription();
#pragma warning restore ASP0018
    }

    private static Task _ServeModuleAsync(HttpContext context, string id, string? path)
    {
        var files = context.RequestServices.GetRequiredService<DashboardAssetCatalog>().FindModule(id);

        // OrdinalIgnoreCase: a module's provider may sit on a case-insensitive file system.
        if (
            files is null
            || path is null
            || string.Equals(path, ModuleManifest.FileName, StringComparison.OrdinalIgnoreCase)
        )
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return Task.CompletedTask;
        }

        return EmbeddedAssetEndpoints.ServeAsync(context, files, path);
    }
}
