// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;
using Headless.Testing.Tests;

namespace Tests.Transport;

public sealed class BrokerReachabilityReporterTests : TestBase
{
    private readonly List<LogMessageEventArgs> _reports = [];

    [Fact]
    public void should_report_every_failure()
    {
        var reporter = new BrokerReachabilityReporter();

        reporter.ReportFailure(_reports.Add, new TimeoutException("first"));
        reporter.ReportFailure(_reports.Add, "second");

        _reports
            .Select(static report => report.LogType)
            .Should()
            .Equal(MqLogType.BrokerUnreachable, MqLogType.BrokerUnreachable);
        _reports.Select(static report => report.Reason).Should().Equal("first", "second");
    }

    [Fact]
    public void should_report_the_first_success_after_a_failure_once()
    {
        var reporter = new BrokerReachabilityReporter();
        reporter.ReportFailure(_reports.Add, "down");
        _reports.Clear();

        reporter.ReportSuccess(_reports.Add);
        reporter.ReportSuccess(_reports.Add);

        _reports.Select(static report => report.LogType).Should().Equal(MqLogType.BrokerReachable);
    }

    [Fact]
    public void should_not_report_a_success_with_no_failure_before_it()
    {
        var reporter = new BrokerReachabilityReporter();

        reporter.ReportSuccess(_reports.Add);

        _reports.Should().BeEmpty();
    }
}
