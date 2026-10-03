// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;

namespace Headless.Jobs;

/// <summary>Jobs-owned context supplied to the exhausted callback.</summary>
/// <param name="JobId">Stable job identity.</param>
/// <param name="FunctionName">Registered function or handler identity.</param>
/// <param name="JobType">The durable job type.</param>
/// <param name="Exception">The exception that ended the run.</param>
/// <param name="RetryCount">The durable retry count consumed.</param>
/// <param name="ServiceProvider">The fresh callback scope.</param>
[PublicAPI]
public sealed record JobExhaustedContext(
    Guid JobId,
    string FunctionName,
    JobType JobType,
    Exception Exception,
    int RetryCount,
    IServiceProvider ServiceProvider
);
