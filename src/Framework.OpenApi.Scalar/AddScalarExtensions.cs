// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Scalar.AspNetCore;

namespace Framework.OpenApi.Scalar;

public static class AddScalarExtensions
{
    public static WebApplication MapFrameworkScalarOpenApi(
        this WebApplication app,
        Action<ScalarOptions>? setupAction = null
    )
    {
        app.MapScalarApiReference(
            endpointPrefix: "/scalar",
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
