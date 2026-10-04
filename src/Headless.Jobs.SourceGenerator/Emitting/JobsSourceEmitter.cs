// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text;
using Headless.Jobs.SourceGenerator.Models;
using Headless.SourceGenerators;

namespace Headless.Jobs.SourceGenerator.Emitting;

#pragma warning disable MA0076 // Generated source lines are clearer as interpolated templates.

/// <summary>
/// Writes the per-assembly registration source from a <see cref="JobsRegistrationModel"/>. This is the generator's
/// only emit path.
/// </summary>
internal static class JobsSourceEmitter
{
    /// <summary>The generated module's type name, in the namespace named after the assembly.</summary>
    public const string ModuleClassName = "JobsModule";

    public static string Emit(JobsRegistrationModel model)
    {
        var writer = new SourceCodeBuilder();
        var jobs = _OrderedByIdentity(model.Jobs).ToList();

        _WriteHeader(writer, model.AssemblyName);
        foreach (var job in jobs)
        {
            writer.AppendLine(
                $"[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute({HandlerSource.Literal(job.Identity)}, {HandlerSource.Literal(job.ContractVersion)})]"
            );
        }

        writer.AppendLine($"namespace {model.AssemblyName}");
        writer.OpenBracket();
        // Public so the host assembly can name it in AddModule<T>(); the registration itself is an explicit interface
        // implementation, reachable only through that call, which runs it once per process.
        writer.AppendLine(
            "/// <summary>Generated Jobs registration for this assembly. Add it with <c>AddModule&lt;JobsModule&gt;()</c>.</summary>"
        );
        writer.AppendLine($"public sealed class {ModuleClassName} : global::Headless.Jobs.IJobsModule");
        writer.OpenBracket();

        var members = new List<Action<SourceCodeBuilder>>
        {
            w => w.AppendLine($"private {ModuleClassName}() {{ }}"),
            w => _WriteRegister(w, model, jobs),
            w => _WriteDescriptorRegistration(w, jobs),
            w => _WriteRequestTypeRegistration(w, jobs),
        };
        foreach (var job in jobs)
        {
            members.Add(w => _WriteInvoker(w, job));
        }

        for (var index = 0; index < members.Count; index++)
        {
            if (index > 0)
            {
                writer.NewLine();
            }

            members[index](writer);
        }

        writer.CloseBracket();
        writer.CloseBracket();

        return writer.ToString();
    }

    private static void _WriteHeader(SourceCodeBuilder writer, string assemblyName)
    {
        writer.AppendLine("//Jobs readonly auto-generated file.");
        writer.AppendLine("#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member");
        writer.NewLine();
        writer.AppendLine("using System;");
        writer.AppendLine("using System.Collections.Generic;");
        writer.AppendLine("using System.Threading;");
        writer.AppendLine("using System.Threading.Tasks;");
        writer.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        writer.AppendLine("using Headless.Jobs;");

        if (!string.IsNullOrEmpty(assemblyName))
        {
            writer.AppendLine($"using {assemblyName};");
        }

        writer.NewLine();
    }

    private static void _WriteRegister(SourceCodeBuilder writer, JobsRegistrationModel model, List<JobModel> jobs)
    {
        writer.AppendLine(
            "static void global::Headless.Jobs.IJobsModule.Register(global::Headless.Jobs.JobsCatalogBuilder catalog)"
        );
        writer.OpenBracket();

        if (jobs.Count > 0)
        {
            writer.AppendLine($"var functions = new Dictionary<string, JobFunctionRegistration>({jobs.Count});");
            foreach (var job in jobs)
            {
                _WriteJobRegistration(writer, job);
            }

            writer.AppendLine("catalog.AddFunctions(functions);");
        }

        writer.AppendLine("RegisterRequestTypes(catalog);");
        writer.AppendLine("RegisterDescriptors(catalog);");
        foreach (var entry in model.Middleware)
        {
            var function = entry.Function is null ? "null" : HandlerSource.Literal(entry.Function);
            var registrationMethod = entry.IsSchedule ? "AddScheduleMiddleware" : "AddExecuteMiddleware";
            writer.AppendLine(
                $"catalog.{registrationMethod}({HandlerSource.Literal(entry.Identity)}, {function}, {entry.Priority}, static (context, next, cancellationToken) => context.Services.GetRequiredService<{entry.TypeName}>().InvokeAsync(context, next, cancellationToken));"
            );
        }

        writer.CloseBracket();
    }

    private static void _WriteJobRegistration(SourceCodeBuilder writer, JobModel job)
    {
        // Bind to the named JobFunctionRegistration record rather than a positional tuple so new per-job knobs stay
        // additive for already-generated code.
        var registration = new StringBuilder()
            .Append("functions.Add(")
            .Append(HandlerSource.Literal(job.Identity))
            .Append(", new JobFunctionRegistration { CronExpression = ")
            .Append(HandlerSource.Literal(job.CronExpression ?? string.Empty))
            .Append(", Priority = (JobPriority)")
            .Append(job.Priority.ToString(CultureInfo.InvariantCulture))
            .Append(", Delegate = ")
            .Append(job.InvokerName)
            .Append(", MaxConcurrency = ")
            .Append(job.MaxConcurrency.ToString(CultureInfo.InvariantCulture))
            .Append(", JobType = typeof(")
            .Append(job.TypeName)
            .Append(')');

        if (job.TimeZone is not null)
        {
            registration.Append(", TimeZoneId = ").Append(HandlerSource.Literal(job.TimeZone));
        }

        registration.Append(_RecoveryKnobs(job));

        // A factory, not the Type, so the runtime never constructs the policy through reflection.
        if (job.FailurePolicyTypeName is { } policyTypeName)
        {
            registration.Append(", FailurePolicy = static () => new ").Append(policyTypeName).Append("()");
        }

        registration.Append(" });");
        writer.AppendLine(registration.ToString());
    }

    /// <summary>
    /// Emits a recovery knob only when the attribute set it. Emitting an unset knob would pin every definition to the
    /// framework default at creation and make the scheduler-wide setting unreachable.
    /// </summary>
    private static string _RecoveryKnobs(JobModel job)
    {
        var knobs = new StringBuilder();
        if (job.OnMissedRun is { } onMissedRun)
        {
            knobs
                .Append(", OnMissedRun = (MissedRunPolicy)")
                .Append(onMissedRun.ToString(CultureInfo.InvariantCulture));
        }

        if (job.MissedRunGraceSeconds is { } graceSeconds)
        {
            knobs.Append(", MissedRunGraceSeconds = ").Append(graceSeconds.ToString(CultureInfo.InvariantCulture));
        }

        if (job.OnOverlap is { } onOverlap)
        {
            knobs.Append(", OnOverlap = (CronOverlapPolicy)").Append(onOverlap.ToString(CultureInfo.InvariantCulture));
        }

        return knobs.ToString();
    }

    /// <summary>
    /// Emits the typed invoker for one job: it builds the job from the run's scope, so constructor dependencies
    /// resolve like any scoped service, calls <c>ExecuteAsync</c> through the job interface so an explicit
    /// implementation works too, and releases the instance it created.
    /// </summary>
    private static void _WriteInvoker(SourceCodeBuilder writer, JobModel job)
    {
        writer.AppendLine(
            $"private static async Task {job.InvokerName}(IServiceProvider serviceProvider, global::Headless.Jobs.JobContext context, CancellationToken cancellationToken)"
        );
        writer.OpenBracket();

        string contextExpression;
        string jobInterface;
        if (job.HasArgs)
        {
            writer.AppendLine(
                $"var request = await JobsRequestProvider.GetRequestAsync<{job.ArgsTypeName}>(context, cancellationToken).ConfigureAwait(false);"
            );
            contextExpression = $"new global::Headless.Jobs.JobContext<{job.ArgsTypeName}>(context, request)";
            jobInterface = $"global::Headless.Jobs.IJob<{job.ArgsTypeName}>";
        }
        else
        {
            contextExpression = "context";
            jobInterface = "global::Headless.Jobs.IJob";
        }

        writer.AppendHandlerInstance(
            job.Disposal,
            "job",
            $"ActivatorUtilities.CreateInstance<{job.TypeName}>(serviceProvider)",
            "job.ConfigureAwait(false)",
            w => _WriteExecute(w, jobInterface, contextExpression)
        );

        writer.CloseBracket();
    }

    private static void _WriteExecute(SourceCodeBuilder writer, string jobInterface, string contextExpression) =>
        writer.AppendLine(
            $"await (({jobInterface})job).ExecuteAsync({contextExpression}, cancellationToken).ConfigureAwait(false);"
        );

    private static void _WriteDescriptorRegistration(SourceCodeBuilder writer, List<JobModel> jobs)
    {
        writer.AppendLine("private static void RegisterDescriptors(global::Headless.Jobs.JobsCatalogBuilder catalog)");
        writer.OpenBracket();

        if (jobs.Count > 0)
        {
            writer.AppendLine($"var descriptors = new Dictionary<string, JobFunctionDescriptor>({jobs.Count});");
            foreach (var job in jobs)
            {
                var identity = HandlerSource.Literal(job.Identity);
                var argsType = job.ArgsTypeName is null ? "null" : $"typeof({job.ArgsTypeName})";
                writer.AppendLine(
                    $"descriptors.Add({identity}, new JobFunctionDescriptor({identity}, {argsType}, {HandlerSource.Literal(job.CronExpression ?? string.Empty)}, (JobPriority){job.Priority.ToString(CultureInfo.InvariantCulture)}, {job.MaxConcurrency.ToString(CultureInfo.InvariantCulture)}, {HandlerSource.Literal(job.ContractVersion)}));"
                );
            }

            writer.AppendLine("catalog.AddDescriptors(descriptors);");
        }

        writer.CloseBracket();
    }

    private static void _WriteRequestTypeRegistration(SourceCodeBuilder writer, List<JobModel> jobs)
    {
        var typedJobs = jobs.Where(job => job.HasArgs).ToList();

        writer.AppendLine("private static void RegisterRequestTypes(global::Headless.Jobs.JobsCatalogBuilder catalog)");
        writer.OpenBracket();

        if (typedJobs.Count > 0)
        {
            writer.AppendLine($"var requestTypes = new Dictionary<string, (string, Type)>({typedJobs.Count});");
            foreach (var job in typedJobs)
            {
                var typeName = job.ArgsTypeName;
                writer.AppendLine(
                    $"requestTypes.Add({HandlerSource.Literal(job.Identity)}, (typeof({typeName}).FullName, typeof({typeName})));"
                );
            }

            writer.AppendLine("catalog.AddRequestTypes(requestTypes);");
        }

        writer.CloseBracket();
    }

    private static IEnumerable<JobModel> _OrderedByIdentity(EquatableArray<JobModel> jobs) =>
        jobs.OrderBy(job => job.Identity, StringComparer.Ordinal);
}

#pragma warning restore MA0076
