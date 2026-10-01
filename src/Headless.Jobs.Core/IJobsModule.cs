// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// One assembly's generated Jobs registration: its <c>[Job]</c> invokers, argument types, descriptors, and
/// middleware. <c>Headless.Jobs.SourceGenerator</c> emits one implementation per assembly as
/// <c>&lt;AssemblyName&gt;.JobsModule</c>.
/// </summary>
/// <remarks>
/// A host lists every module it runs with
/// <see cref="JobsOptionsBuilder{TTimeJob,TCronJob}.AddModule{TModule}"/> inside the <c>AddHeadlessJobs</c> callback,
/// or a module contributes itself with <see cref="JobsContributionBuilder.AddModule{TModule}"/> through
/// <c>services.ConfigureJobs(...)</c>, before or after <c>AddHeadlessJobs</c>. Nothing registers implicitly: an assembly
/// whose module is not added contributes no functions or middleware, even when it is loaded.
/// </remarks>
public interface IJobsModule
{
    /// <summary>
    /// Adds this module's registrations to one host's catalog. The Jobs setup calls it once per host, when the host's
    /// job registry is built, for every added or contributed module; do not call it directly.
    /// </summary>
    /// <param name="catalog">The catalog of the host being built.</param>
    static abstract void Register(JobsCatalogBuilder catalog);
}
