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
    public const string FailurePolicyMetadataName = "Headless.Reliability.IFailurePolicy";
    public const string ScheduleMiddlewareAttributeMetadataName = "Headless.Jobs.JobScheduleMiddlewareAttribute`1";
    public const string ExecuteMiddlewareAttributeMetadataName = "Headless.Jobs.JobExecuteMiddlewareAttribute`1";
    public const string DescriptorMetadataAttributeName = "Headless.Jobs.JobFunctionDescriptorMetadataAttribute";

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
}
