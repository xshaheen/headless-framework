// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;

namespace Tests;

internal static class CircuitBreakerTestHelpers
{
    public static IMeterFactory CreateMeterFactory()
    {
        var meter = new Meter("Headless.Messaging.Test");
        var factory = Substitute.For<IMeterFactory>();
        factory.Create(Arg.Any<MeterOptions>()).Returns(meter);
        return factory;
    }
}
