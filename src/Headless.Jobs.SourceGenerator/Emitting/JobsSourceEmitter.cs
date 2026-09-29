// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text;
using Headless.Jobs.SourceGenerator.Models;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis.CSharp;

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
        var functions = model.Functions;
        var hasTypedFunctions = functions.Any(function => function.UsesGenericContext);

        _WriteHeader(writer, model.AssemblyName, hasTypedFunctions);
        foreach (var function in _OrderedByName(functions))
        {
            writer.AppendLine(
                $"[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute({_Literal(function.FunctionName)}, {_Literal(function.ContractVersion)})]"
            );
        }

        writer.AppendLine($"namespace {model.AssemblyName}");
        writer.OpenBracket();
        // Public so the host assembly can name it in AddModule<T>(); the registration itself is an explicit interface
        // implementation, reachable only through that call, which runs it once per process.
        writer.AppendLine(
            "/// <summary>Generated Jobs registration for this assembly. Add it with <c>AddModule&lt;JobsModule&gt;()</c> inside <c>AddHeadlessJobs</c>.</summary>"
        );
        writer.AppendLine($"public sealed class {ModuleClassName} : global::Headless.Jobs.IJobsModule");
        writer.OpenBracket();

        var members = new List<Action<SourceCodeBuilder>>
        {
            w => w.AppendLine($"private {ModuleClassName}() {{ }}"),
            w => _WriteRegister(w, model),
            w => _WriteDescriptorRegistration(w, functions),
        };
        foreach (var jobClass in _ConstructedClasses(functions))
        {
            members.Add(w => _WriteFactoryMethod(w, jobClass));
        }

        if (hasTypedFunctions)
        {
            members.Add(_WriteGenericContextHelper);
        }

        members.Add(w => _WriteRequestTypeRegistration(w, model));
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
        _WriteAppJobs(writer, functions, model.AssemblyName);

        return writer.ToString();
    }

    private static void _WriteHeader(SourceCodeBuilder writer, string assemblyName, bool hasTypedFunctions)
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
        writer.AppendLine("using Headless.Jobs.Enums;");
        if (hasTypedFunctions)
        {
            writer.AppendLine("using Headless.Jobs.Base;");
        }

        if (!string.IsNullOrEmpty(assemblyName))
        {
            writer.AppendLine($"using {assemblyName};");
        }

        writer.NewLine();
    }

    private static void _WriteRegister(SourceCodeBuilder writer, JobsRegistrationModel model)
    {
        var functions = model.Functions;
        writer.AppendLine("static void global::Headless.Jobs.IJobsModule.Register()");
        writer.OpenBracket();

        if (functions.Count > 0)
        {
            writer.AppendLine(
                $"var jobFunctionDelegateDict = new Dictionary<string, JobFunctionRegistration>({functions.Count});"
            );
            foreach (var function in functions)
            {
                _WriteFunctionRegistration(writer, function);
            }

            writer.AppendLine($"JobFunctionProvider.RegisterFunctions(jobFunctionDelegateDict, {functions.Count});");
        }

        writer.AppendLine("RegisterRequestTypes();");
        writer.AppendLine("RegisterDescriptors();");
        foreach (var entry in model.Middleware)
        {
            var function = entry.Function is null ? "null" : _Literal(entry.Function);
            var registrationMethod = entry.IsSchedule ? "RegisterSchedule" : "RegisterExecute";
            writer.AppendLine(
                $"JobMiddlewareRegistry.{registrationMethod}({_Literal(entry.Identity)}, {function}, {entry.Priority}, static (context, next, cancellationToken) => context.Services.GetRequiredService<{entry.TypeName}>().InvokeAsync(context, next, cancellationToken));"
            );
        }

        writer.CloseBracket();
    }

    private static void _WriteFunctionRegistration(SourceCodeBuilder writer, JobFunctionModel function)
    {
        // Only async when the body awaits: the typed-context conversion or an awaitable method.
        var asyncFlag = function.UsesGenericContext || function.IsAwaitable ? "async " : "";
        var cronExpression = string.IsNullOrEmpty(function.CronExpression)
            ? "string.Empty"
            : _Literal(function.CronExpression);

        // Bind to the named JobFunctionRegistration record rather than a positional tuple so new per-function knobs
        // stay additive for already-generated code.
        writer.AppendLine(
            $"jobFunctionDelegateDict.Add({_Literal(function.FunctionName)}, new JobFunctionRegistration {{ CronExpression = {cronExpression}, Priority = (JobPriority){function.Priority}, Delegate = new JobFunctionDelegate({asyncFlag}(serviceProvider, context, cancellationToken) =>"
        );
        writer.OpenBracket();

        if (function.UsesGenericContext)
        {
            writer.AppendLine(
                $"var genericContext = await ToGenericContextWithRequest<{function.RequestTypeName}>(context, cancellationToken);"
            );
        }

        var call = $"{_Receiver(function)}.{function.MethodName}({string.Join(", ", function.InvocationArguments)})";
        if (function.IsAwaitable)
        {
            writer.AppendLine($"await {call};");
        }
        else if (function.UsesGenericContext)
        {
            // The lambda is already async for the typed-context conversion, so it completes without returning a task.
            writer.AppendLine($"{call};");
        }
        else
        {
            writer.AppendLine($"{call};");
            writer.AppendLine("return Task.CompletedTask;");
        }

        writer.AppendLine($"}}), MaxConcurrency = {function.MaxConcurrency}{_RecoveryKnobs(function)} }});");
    }

    /// <summary>
    /// Emits a recovery knob only when the attribute set it. Emitting an unset knob would pin every definition to the
    /// framework default at creation and make the scheduler-wide setting unreachable.
    /// </summary>
    private static string _RecoveryKnobs(JobFunctionModel function)
    {
        var knobs = new StringBuilder();
        if (function.OnMissedRun is { } onMissedRun)
        {
            knobs
                .Append(", OnMissedRun = (MissedRunPolicy)")
                .Append(onMissedRun.ToString(CultureInfo.InvariantCulture));
        }

        if (function.MissedRunGraceSeconds is { } graceSeconds)
        {
            knobs.Append(", MissedRunGraceSeconds = ").Append(graceSeconds.ToString(CultureInfo.InvariantCulture));
        }

        if (function.OnOverlap is { } onOverlap)
        {
            knobs.Append(", OnOverlap = (CronOverlapPolicy)").Append(onOverlap.ToString(CultureInfo.InvariantCulture));
        }

        return knobs.ToString();
    }

    /// <summary>The call receiver: the class for static methods, otherwise the generated factory.</summary>
    private static string _Receiver(JobFunctionModel function) =>
        function.IsStaticMethod ? function.Class.TypeName : $"{function.Class.FactoryMethodName}(serviceProvider)";

    private static void _WriteDescriptorRegistration(
        SourceCodeBuilder writer,
        EquatableArray<JobFunctionModel> functions
    )
    {
        writer.AppendLine("private static void RegisterDescriptors()");
        writer.OpenBracket();

        if (functions.Count > 0)
        {
            writer.AppendLine($"var descriptors = new Dictionary<string, JobFunctionDescriptor>({functions.Count});");
            foreach (var function in _OrderedByName(functions))
            {
                var functionName = _Literal(function.FunctionName);
                var value = function.RequestTypeName is null
                    ? $"AppJobs.{_GetHandleName(function.FunctionName ?? string.Empty)}"
                    : $"new JobFunctionDescriptor({functionName}, typeof({function.RequestTypeName}), {_Literal(function.CronExpression ?? string.Empty)}, (JobPriority){function.Priority}, {function.MaxConcurrency}, {_Literal(function.ContractVersion)})";
                writer.AppendLine($"descriptors.Add({functionName}, {value});");
            }

            writer.AppendLine($"JobFunctionProvider.RegisterDescriptors(descriptors, {functions.Count});");
        }

        writer.CloseBracket();
    }

    private static void _WriteFactoryMethod(SourceCodeBuilder writer, JobClassModel jobClass)
    {
        writer.AppendLine(
            $"private static {jobClass.TypeName} {jobClass.FactoryMethodName}(IServiceProvider serviceProvider)"
        );
        writer.OpenBracket();
        foreach (var parameter in jobClass.ConstructorParameters)
        {
            if (parameter.TypeName is null)
            {
                continue;
            }

            writer.AppendLine(
                parameter.ServiceKey is null
                    ? $"var {parameter.Name} = serviceProvider.GetService<{parameter.TypeName}>();"
                    : $"var {parameter.Name} = serviceProvider.GetKeyedService<{parameter.TypeName}>({parameter.ServiceKey});"
            );
        }

        writer.AppendLine(
            $"return new {jobClass.TypeName}({string.Join(", ", jobClass.ConstructorParameters.Select(x => x.Name))});"
        );
        writer.CloseBracket();
    }

    private static void _WriteGenericContextHelper(SourceCodeBuilder writer)
    {
        writer.AppendLine(
            "private static async Task<JobFunctionContext<T>> ToGenericContextWithRequest<T>(JobFunctionContext context, CancellationToken cancellationToken)"
        );
        writer.OpenBracket();
        writer.AppendLine("var request = await JobsRequestProvider.GetRequestAsync<T>(context, cancellationToken);");
        writer.AppendLine("return new JobFunctionContext<T>(context, request);");
        writer.CloseBracket();
    }

    private static void _WriteRequestTypeRegistration(SourceCodeBuilder writer, JobsRegistrationModel model)
    {
        var typedFunctions = model.Functions.Where(function => function.UsesGenericContext).ToList();

        writer.AppendLine("private static void RegisterRequestTypes()");
        writer.OpenBracket();

        if (typedFunctions.Count > 0)
        {
            writer.AppendLine($"var requestTypes = new Dictionary<string, (string, Type)>({typedFunctions.Count});");
            foreach (var function in typedFunctions)
            {
                var typeName = function.RequestTypeName;
                writer.AppendLine(
                    $"requestTypes.Add({_Literal(function.FunctionName)}, (typeof({typeName}).FullName, typeof({typeName})));"
                );
            }

            writer.AppendLine($"JobFunctionProvider.RegisterRequestType(requestTypes, {typedFunctions.Count});");
        }

        writer.CloseBracket();
    }

    private static void _WriteAppJobs(
        SourceCodeBuilder writer,
        EquatableArray<JobFunctionModel> functions,
        string assemblyName
    )
    {
        var requestless = _OrderedByName(functions).Where(function => function.RequestTypeName is null).ToList();
        if (requestless.Count == 0)
        {
            return;
        }

        writer.NewLine();
        writer.AppendLine($"namespace {assemblyName}");
        writer.OpenBracket();
        writer.AppendLine("/// <summary>Canonical generated handles for this assembly's requestless jobs.</summary>");
        writer.AppendLine("public static class AppJobs");
        writer.OpenBracket();
        foreach (var function in requestless)
        {
            var functionName = function.FunctionName ?? string.Empty;
            writer.AppendLine("/// <summary>A canonical requestless job descriptor.</summary>");
            writer.AppendLine(
                $"public static JobFunctionDescriptor {_GetHandleName(functionName)} {{ get; }} = new JobFunctionDescriptor({_Literal(functionName)}, null, {_Literal(function.CronExpression ?? string.Empty)}, (JobPriority){function.Priority}, {function.MaxConcurrency}, {_Literal(function.ContractVersion)});"
            );
        }

        writer.CloseBracket();
        writer.CloseBracket();
    }

    /// <summary>Distinct constructed classes in first-use order; static classes need no factory.</summary>
    private static IEnumerable<JobClassModel> _ConstructedClasses(EquatableArray<JobFunctionModel> functions)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in functions)
        {
            if (!function.Class.IsStatic && seen.Add(function.Class.TypeName))
            {
                yield return function.Class;
            }
        }
    }

    private static IEnumerable<JobFunctionModel> _OrderedByName(EquatableArray<JobFunctionModel> functions) =>
        functions.OrderBy(function => function.FunctionName, StringComparer.Ordinal);

    private static string _Literal(string? value) =>
        value is null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true);

    private static string _GetHandleName(string contract)
    {
        // Encode underscores too, so literal escape-looking contracts cannot collide with encoded punctuation.
        // Reserved members encode their first character, which cannot collide with a valid unescaped identifier.
        var reserved =
            contract
            is "AppJobs"
                or "Equals"
                or "ReferenceEquals"
                or "GetHashCode"
                or "GetType"
                or "ToString"
                or "MemberwiseClone"
                or "Finalize";
        var result = new StringBuilder();
        for (var index = 0; index < contract.Length; index++)
        {
            var character = contract[index];
            if (
                (
                    (character >= 'A' && character <= 'Z')
                    || (character >= 'a' && character <= 'z')
                    || (index > 0 && character >= '0' && character <= '9')
                ) && !(reserved && index == 0)
            )
            {
                result.Append(character);
            }
            else
            {
                result.Append("_u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture)).Append('_');
            }
        }

        var identifier = result.ToString();
        return
            SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None
            || SyntaxFacts.GetContextualKeywordKind(identifier) != SyntaxKind.None
            ? "@" + identifier
            : identifier;
    }
}

#pragma warning restore MA0076
