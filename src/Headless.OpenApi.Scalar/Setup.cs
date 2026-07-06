// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Routing;
using Scalar.AspNetCore;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Registration helper for mounting the Scalar API reference UI.
/// </summary>
[PublicAPI]
public static class SetupScalar
{
    /// <summary>
    /// Maps the Scalar API reference UI at the specified endpoint prefix.
    /// </summary>
    /// <param name="app">The endpoint route builder to register the Scalar UI on.</param>
    /// <param name="setupAction">
    /// Optional callback to override Scalar display options after Headless defaults are applied.
    /// When <see langword="null"/>, the Headless defaults are used as-is.
    /// </param>
    /// <param name="endpointPrefix">
    /// The URL prefix at which the Scalar UI is served. Default is <c>/scalar</c>.
    /// </param>
    /// <returns>The same <paramref name="app"/> instance for chaining.</returns>
    /// <remarks>
    /// Headless defaults applied before <paramref name="setupAction"/> is called:
    /// <list type="bullet">
    ///   <item><description>OpenAPI document route pattern: <c>/openapi/{documentName}.json</c></description></item>
    ///   <item><description>Dark mode enabled; toggle visible</description></item>
    ///   <item><description>Modern layout; tags sorted alphabetically; operations sorted by HTTP method</description></item>
    ///   <item><description>
    ///     Code generation targets: C#, Go, JavaScript, Node.js, PowerShell, Shell (curl)
    ///   </description></item>
    ///   <item><description>
    ///     HTTP clients: HttpClient, curl, Axios, Fetch, XHR, WebRequest, wget, HTTPie
    ///   </description></item>
    /// </list>
    /// </remarks>
    public static IEndpointRouteBuilder MapScalarOpenApi(
        this IEndpointRouteBuilder app,
        Action<ScalarOptions>? setupAction = null,
        string endpointPrefix = "/scalar"
    )
    {
        app.MapScalarApiReference(
            endpointPrefix: endpointPrefix,
            (options, _) =>
            {
                options.OpenApiRoutePattern = "/openapi/{documentName}.json";
                options.DarkMode = true;
                options.HideDarkModeToggle = false;
                options.Layout = ScalarLayout.Modern;
                options.TagSorter = TagSorter.Alpha;
                options.OperationSorter = OperationSorter.Method;

                options.EnabledTargets =
                [
                    ScalarTarget.CSharp,
                    ScalarTarget.Go,
                    ScalarTarget.JavaScript,
                    ScalarTarget.Node,
                    ScalarTarget.PowerShell,
                    ScalarTarget.Shell,
                ];

                options.EnabledClients =
                [
                    ScalarClient.HttpClient,
                    ScalarClient.Curl,
                    ScalarClient.Axios,
                    ScalarClient.Fetch,
                    ScalarClient.Xhr,
                    ScalarClient.WebRequest,
                    ScalarClient.Wget,
                    ScalarClient.Httpie,
                ];

                setupAction?.Invoke(options);
            }
        );

        return app;
    }
}
