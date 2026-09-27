// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.SourceGenerator.Utilities;

/// <summary>
/// Constants used throughout the Jobs source generator.
/// </summary>
internal static class SourceGeneratorConstants
{
    public const string JobFunctionAttributeMetadataName = "Headless.Jobs.Base.JobFunctionAttribute";
    public const string ScheduleMiddlewareAttributeMetadataName = "Headless.Jobs.JobScheduleMiddlewareAttribute`1";
    public const string ExecuteMiddlewareAttributeMetadataName = "Headless.Jobs.JobExecuteMiddlewareAttribute`1";
    public const string DescriptorMetadataAttributeName = "Headless.Jobs.JobFunctionDescriptorMetadataAttribute";
    public const string CancellationTokenTypeName = "System.Threading.CancellationToken";
    public const string BaseJobFunctionContextTypeName = "Headless.Jobs.Base.JobFunctionContext";
    public const string FromKeyedServicesAttributeName = "FromKeyedServicesAttribute";

    /// <summary>
    /// Assembly name for which nothing is generated. No assembly in this repository has this name; the exclusion
    /// predates the incremental rebuild and is kept so the rebuild changes no behavior.
    /// </summary>
    public const string ExcludedAssemblyName = "Jobs";

    public const string GeneratedFileName = "JobsInstanceFactory.g.cs";
    public const string ConfigExpressionPrefix = "%";
    public const string ConfigExpressionSuffix = "%";
    public const int MinConfigExpressionLength = 2;
}
