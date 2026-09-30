// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Jobs;
using Headless.Jobs.Base;
using Microsoft.Extensions.Logging;

namespace Tests.Logging;

public sealed partial class JobsLoggerMessageEventIdTests
{
    /// <summary>Every shipped Jobs assembly that can declare log messages.</summary>
    private static readonly Assembly[] _JobsAssemblies =
    [
        typeof(JobAttribute).Assembly,
        typeof(JobFunctionProvider).Assembly,
        typeof(SetupJobsDashboard).Assembly,
        typeof(SetupJobsEntityFramework).Assembly,
        typeof(SetupPostgreSqlJobsEntityFramework).Assembly,
        typeof(SetupSqlServerJobsEntityFramework).Assembly,
    ];

    [Fact]
    public void should_give_every_jobs_log_message_its_own_event_id()
    {
        LoggerMessageEventIdAudit.FindViolations(_JobsAssemblies).Should().BeEmpty();
    }

    [Fact]
    public void should_report_shared_and_missing_event_ids()
    {
        var violations = LoggerMessageEventIdAudit.FindViolations([typeof(JobsLoggerMessageEventIdTests).Assembly]);

        violations
            .Should()
            .Contain(violation =>
                violation.StartsWith("EventId 9001 is shared by:", StringComparison.Ordinal)
                && violation.Contains($"{nameof(FirstOwner)}.{nameof(FirstOwner.Shared)}", StringComparison.Ordinal)
                && violation.Contains($"{nameof(SecondOwner)}.{nameof(SecondOwner.Shared)}", StringComparison.Ordinal)
            )
            .And.Contain(violation =>
                violation.StartsWith("No EventId:", StringComparison.Ordinal)
                && violation.Contains(nameof(SecondOwner.WithoutId), StringComparison.Ordinal)
            );
    }

    // Deliberate violations the audit must detect; they are never logged. The duplicates live in different types
    // because the logging generator already rejects a duplicate within one type (SYSLIB1006); collisions across types
    // and assemblies are what only this audit catches.
    internal static partial class FirstOwner
    {
        [LoggerMessage(EventId = 9001, Level = LogLevel.Debug, Message = "first")]
        public static partial void Shared(ILogger logger);
    }

    internal static partial class SecondOwner
    {
        [LoggerMessage(EventId = 9001, Level = LogLevel.Debug, Message = "second")]
        public static partial void Shared(ILogger logger);

        [LoggerMessage(Level = LogLevel.Debug, Message = "no id")]
        public static partial void WithoutId(ILogger logger);
    }
}
