// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using OpenTelemetry;

namespace Headless.Logging.Internal;

/// <summary>
/// Replaces a declared credential path in the path tags of every span the tracer provider records, server and client
/// alike, when the span ends and every instrumentation has set its tags.
/// </summary>
/// <remarks>Must run before the export processors, so it is added before any exporter.</remarks>
internal sealed class LogPathRedactionActivityProcessor(ILogPathRedactor[] redactors) : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        if (redactors.Length > 0)
        {
            LogPathTags.Redact(data, redactors);
        }
    }
}
