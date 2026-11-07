// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Context for the examples in docs/llms/dashboards.md: the namespaces of the Headless packages the guide documents,
// and placeholder application types the examples name but do not declare. An example that declares a type of the same
// name uses its own.

global using Headless.Dashboard;
global using Headless.Messaging;
global using Headless.Messaging.Dashboard;

namespace DocsPrelude
{
    public static class ReportsEndpoints
    {
        public static IResult GetRuns() => Results.Ok(Array.Empty<string>());

        public static IResult CancelRun(string id) => Results.NoContent();
    }
}
