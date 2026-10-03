// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

/// <summary>One <c>Tune</c> call recorded in the service collection.</summary>
internal sealed record JobsTuningContribution(JobTuning Tuning);
