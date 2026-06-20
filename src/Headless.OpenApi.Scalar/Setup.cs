// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Routing;
using Scalar.AspNetCore;

namespace Headless.Api;

public static class SetupScalar
{
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
