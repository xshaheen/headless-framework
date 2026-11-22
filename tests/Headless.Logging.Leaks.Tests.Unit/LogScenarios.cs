// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Tests;

/// <summary>One way application or framework code puts a sensitive value into a log entry.</summary>
/// <param name="Id">The matrix row id, also the scenario's marker and value suffix.</param>
/// <param name="Path">What the row exercises, as the ADR matrix names it.</param>
/// <param name="Sentinel">The sensitive value the scenario writes.</param>
/// <param name="Native">Written through <see cref="Serilog.ILogger"/> instead of <see cref="ILogger"/>.</param>
/// <param name="Write">Writes the entry.</param>
internal sealed record LogScenario(string Id, string Path, string Sentinel, bool Native, Action<LogLeakHost> Write)
{
    public override string ToString() => Id;
}

#pragma warning disable CA1848, CA2254 // False positive for this suite: the scenarios reproduce the non-generated, non-constant calls consumer code makes, which is the leak being measured.
internal static class LogScenarios
{
    public static readonly IReadOnlyDictionary<string, LogScenario> All = new LogScenario[]
    {
        new(
            "m1",
            "message template: literal text, a value with a known shape (national ID)",
            Pii.NationalId(1),
            Native: false,
            h => h.Logger.LogWarning("marker-m1 lookup " + Pii.NationalId(1))
        ),
        new(
            "m2",
            "message template: literal text, a value with no shape (API key)",
            Pii.Token("m2"),
            Native: false,
            h => h.Logger.LogWarning("marker-m2 token " + Pii.Token("m2"))
        ),
        new(
            "m3",
            "structured arg: [LoggerMessage] parameter classified [PrivateData]",
            Pii.Email("m3"),
            Native: false,
            h => LeakLog.ClassifiedParameter(h.Logger, Pii.Email("m3"))
        ),
        new(
            "m4",
            "structured arg: plain {Email} hole, name classified by ClassifyProperty",
            Pii.Email("m4"),
            Native: false,
            h => h.Logger.LogWarning("marker-m4 signup {Email}", Pii.Email("m4"))
        ),
        new(
            "m5",
            "structured arg: plain {Contact} hole, unclassified name",
            Pii.Email("m5"),
            Native: false,
            h => h.Logger.LogWarning("marker-m5 signup {Contact}", Pii.Email("m5"))
        ),
        new(
            "m6",
            "destructured object: {@Customer} with a [PrivateData] member",
            Pii.Email("m6"),
            Native: false,
            h => h.Logger.LogWarning("marker-m6 created {@Customer}", new LeakCustomer(Pii.Email("m6"), "gold"))
        ),
        new(
            "m7",
            "scope: dictionary scope, Email key (classified name)",
            Pii.Email("m7"),
            Native: false,
            h =>
            {
                using (
                    h.Logger.BeginScope(
                        new Dictionary<string, object?>(StringComparer.Ordinal) { ["Email"] = Pii.Email("m7") }
                    )
                )
                {
                    h.Logger.LogWarning("marker-m7");
                }
            }
        ),
        new(
            "m8",
            "scope: message-template scope, {Contact} (unclassified name)",
            Pii.Email("m8"),
            Native: false,
            h =>
            {
                using (h.Logger.BeginScope("tenant {Contact}", Pii.Email("m8")))
                {
                    h.Logger.LogWarning("marker-m8");
                }
            }
        ),
        new(
            "m9",
            "exception text: message with a known shape (national ID)",
            Pii.NationalId(9),
            Native: false,
            h => h.Logger.LogWarning(new InvalidOperationException("failed for " + Pii.NationalId(9)), "marker-m9")
        ),
        new(
            "m10",
            "exception text: message with no shape (API key)",
            Pii.Token("m10"),
            Native: false,
            h => h.Logger.LogWarning(new InvalidOperationException("rejected key " + Pii.Token("m10")), "marker-m10")
        ),
        new(
            "m12",
            "structured arg: [LogProperties] object with a [PrivateData] member",
            Pii.Email("m12"),
            Native: false,
            h => LeakLog.LogProperties(h.Logger, new LeakCustomer(Pii.Email("m12"), "gold"))
        ),
        new(
            "m13",
            "Serilog LogContext.PushProperty(\"Email\") around an ILogger call (enrichment after the factory)",
            Pii.Email("m13"),
            Native: false,
            h =>
            {
                using (LogContext.PushProperty("Email", Pii.Email("m13")))
                {
                    h.Logger.LogWarning("marker-m13");
                }
            }
        ),
        new(
            "n1",
            "native Serilog: {Email} hole (classified name)",
            Pii.Email("n1"),
            Native: true,
            h => h.SerilogLogger.Warning("marker-n1 signup {Email}", Pii.Email("n1"))
        ),
        new(
            "n2",
            "native Serilog: {@Customer} with a [PrivateData] member",
            Pii.Email("n2"),
            Native: true,
            h => h.SerilogLogger.Warning("marker-n2 created {@Customer}", new LeakCustomer(Pii.Email("n2"), "gold"))
        ),
        new(
            "n3",
            "native Serilog: literal template text with a known shape",
            Pii.NationalId(103),
            Native: true,
            h => h.SerilogLogger.Warning("marker-n3 lookup " + Pii.NationalId(103))
        ),
        new(
            "n4",
            "native Serilog: LogContext.PushProperty(\"Email\")",
            Pii.Email("n4"),
            Native: true,
            h =>
            {
                using (LogContext.PushProperty("Email", Pii.Email("n4")))
                {
                    h.SerilogLogger.Warning("marker-n4");
                }
            }
        ),
        new(
            "n5",
            "native Serilog: ForContext(\"Email\")",
            Pii.Email("n5"),
            Native: true,
            h => h.SerilogLogger.ForContext("Email", Pii.Email("n5")).Warning("marker-n5")
        ),
        new(
            "n6",
            "native Serilog: exception message with a known shape",
            Pii.NationalId(106),
            Native: true,
            h =>
                h.SerilogLogger.Warning(new InvalidOperationException("failed for " + Pii.NationalId(106)), "marker-n6")
        ),
        new(
            "n7",
            "native Serilog: {Contact} hole (unclassified name)",
            Pii.Email("n7"),
            Native: true,
            h => h.SerilogLogger.Warning("marker-n7 signup {Contact}", Pii.Email("n7"))
        ),
    }.ToDictionary(s => s.Id, StringComparer.Ordinal);
}
#pragma warning restore CA1848, CA2254
