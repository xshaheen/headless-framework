// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.SourceGenerator.Utilities;

/// <summary>
/// Constants used throughout the Jobs source generator.
/// </summary>
internal static class SourceGeneratorConstants
{
    public const string JobAttributeMetadataName = "Headless.Jobs.Base.JobAttribute";
    public const string JobInterfaceMetadataName = "Headless.Jobs.Base.IJob";
    public const string GenericJobInterfaceMetadataName = "Headless.Jobs.Base.IJob`1";
    public const string ScheduleMiddlewareAttributeMetadataName = "Headless.Jobs.JobScheduleMiddlewareAttribute`1";
    public const string ExecuteMiddlewareAttributeMetadataName = "Headless.Jobs.JobExecuteMiddlewareAttribute`1";
    public const string DescriptorMetadataAttributeName = "Headless.Jobs.JobFunctionDescriptorMetadataAttribute";
    public const string FailurePolicyMetadataName = "Headless.Reliability.FailurePolicy";

    /// <summary>
    /// Assembly name for which nothing is generated. No assembly in this repository has this name; the exclusion
    /// predates the incremental rebuild and is kept so the rebuild changes no behavior.
    /// </summary>
    public const string ExcludedAssemblyName = "Jobs";

    public const string GeneratedFileName = "JobsModule.g.cs";

    /// <summary>
    /// A cron value starting with this prefix names an <c>IConfiguration</c> key. The runtime resolves it at startup,
    /// so the generator leaves it unvalidated.
    /// </summary>
    public const string ConfigExpressionPrefix = "%";

    // The generator ships as a netstandard2.0 analyzer and cannot reference Headless.Jobs.Abstractions, so these mirror
    // the runtime JobPriority, MissedRunPolicy, and CronOverlapPolicy values and JobContract.InitialVersion. Change them
    // together with those types, or the build-time checks and defaults drift from what the runtime accepts.

    /// <summary><c>JobPriority.Normal</c>, the lowest value and the default priority.</summary>
    public const int NormalJobPriority = 0;

    /// <summary><c>JobPriority.LongRunning</c>, the highest defined priority.</summary>
    public const int LongRunningJobPriority = 3;

    /// <summary><c>MissedRunPolicy.Coalesce</c>.</summary>
    public const int CoalesceMissedRunPolicy = 0;

    /// <summary><c>MissedRunPolicy.Skip</c>.</summary>
    public const int SkipMissedRunPolicy = 1;

    /// <summary><c>CronOverlapPolicy.Allow</c>.</summary>
    public const int AllowOverlapPolicy = 0;

    /// <summary><c>CronOverlapPolicy.Skip</c>.</summary>
    public const int SkipOverlapPolicy = 1;

    /// <summary><c>JobContract.InitialVersion</c>, the contract version of a job that declares none.</summary>
    public const string InitialContractVersion = "1";
}
