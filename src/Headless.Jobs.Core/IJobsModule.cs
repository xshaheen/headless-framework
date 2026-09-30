// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// One assembly's generated Jobs registration: its <c>[JobFunction]</c> delegates, request types, descriptors, and
/// middleware. <c>Headless.Jobs.SourceGenerator</c> emits one implementation per assembly as
/// <c>&lt;AssemblyName&gt;.JobsModule</c>.
/// </summary>
/// <remarks>
/// A host lists every module it runs with
/// <see cref="JobsOptionsBuilder{TTimeJob,TCronJob}.AddModule{TModule}"/> inside the <c>AddHeadlessJobs</c> callback,
/// or a module contributes itself with <see cref="JobsContributionBuilder.AddModule{TModule}"/> through
/// <c>services.ConfigureJobs(...)</c>. Nothing registers implicitly: an assembly whose module is not added contributes no functions or middleware, even when
/// it is loaded.
/// </remarks>
public interface IJobsModule
{
    /// <summary>
    /// Adds this module's registrations to the process-wide catalog. The Jobs setup calls it once per process for every
    /// added or contributed module; do not call it directly.
    /// </summary>
    static abstract void Register();
}
