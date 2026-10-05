// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>Receives the progress a running job reports through <see cref="JobContext.ReportProgress"/>.</summary>
internal interface IJobProgressSink
{
    /// <summary>Records <paramref name="progress"/> as the run's latest report; never blocks on the store.</summary>
    void Report(JobProgress progress);
}
