// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;

namespace Tests.CircuitBreaker;

internal static class CircuitBreakerTestHelpers
{
    /// <summary>
    /// Creates a real <see cref="IMeterFactory"/> that owns the served meter and disposes it when
    /// the factory is disposed — mirroring the DI-container ownership model used in production.
    /// Callers own the returned factory and must dispose it.
    /// </summary>
    public static IMeterFactory CreateMeterFactory() => new TestMeterFactory();

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly Meter _meter = new("Headless.Messaging.Test");

        public Meter Create(MeterOptions options) => _meter;

        public void Dispose() => _meter.Dispose();
    }
}
