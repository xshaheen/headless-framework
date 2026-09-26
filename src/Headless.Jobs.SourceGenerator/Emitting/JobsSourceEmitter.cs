// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text;
using Headless.Jobs.SourceGenerator.Building;
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
    public static string Emit(JobsRegistrationModel model)
    {
        var writer = new SourceWriter();
        var functions = model.Functions;
        var hasTypedFunctions = functions.Any(function => function.UsesGenericContext);

        _WriteHeader(writer, model.AssemblyName, hasTypedFunctions);
        foreach (var function in _OrderedByName(functions))
        {
            writer.WriteLine(
                $"[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute({_Literal(function.FunctionName)}, {_Literal(function.ContractVersion)})]"
            );
        }

        writer.WriteLine($"namespace {model.AssemblyName}");
        writer.OpenBlock();
        // Internal: this registration class is invoked only by its own [ModuleInitializer], so it never needs to
        // appear on the consuming assembly's public surface.
        writer.WriteLine("internal static class JobsInstanceFactoryExtensions");
        writer.OpenBlock();

        var members = new List<Action<SourceWriter>>
        {
            w => _WriteInitialize(w, model),
            w => _WriteDescriptorRegistration(w, functions),
        };
        foreach (var jobClass in _ConstructedClasses(functions))
        {
            members.Add(w => _WriteFactoryMethod(w, jobClass, model.AssemblyName));
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
                writer.WriteLine();
            }

            members[index](writer);
        }

        writer.CloseBlock();
        writer.CloseBlock();
        _WriteAppJobs(writer, functions, model.AssemblyName);

        return writer.ToString();
    }

    private static void _WriteHeader(SourceWriter writer, string assemblyName, bool hasTypedFunctions)
    {
        writer.WriteLine("//Jobs readonly auto-generated file.");
        writer.WriteLine("#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member");
        writer.WriteLine();
        writer.WriteLine("using System;");
        writer.WriteLine("using System.Collections.Generic;");
        writer.WriteLine("using System.Threading;");
        writer.WriteLine("using System.Threading.Tasks;");
        writer.WriteLine("using Microsoft.Extensions.DependencyInjection;");
        writer.WriteLine("using Headless.Jobs;");
        writer.WriteLine("using Headless.Jobs.Enums;");
        if (hasTypedFunctions)
        {
            writer.WriteLine("using Headless.Jobs.Base;");
        }

        if (!string.IsNullOrEmpty(assemblyName))
        {
            writer.WriteLine($"using {assemblyName};");
        }

        writer.WriteLine();
    }

    private static void _WriteInitialize(SourceWriter writer, JobsRegistrationModel model)
    {
        var functions = model.Functions;
        writer.WriteLine("[global::System.Runtime.CompilerServices.ModuleInitializer]");
        writer.WriteLine("public static void Initialize()");
        writer.OpenBlock();

        if (functions.Count > 0)
        {
            writer.WriteLine(
                $"var jobFunctionDelegateDict = new Dictionary<string, JobFunctionRegistration>({functions.Count});"
            );
            foreach (var function in functions)
            {
                _WriteFunctionRegistration(writer, function, model);
            }

            writer.WriteLine($"JobFunctionProvider.RegisterFunctions(jobFunctionDelegateDict, {functions.Count});");
        }

        writer.WriteLine("RegisterRequestTypes();");
        writer.WriteLine("RegisterDescriptors();");
        foreach (var entry in model.Middleware)
        {
            var function = entry.Function is null ? "null" : _Literal(entry.Function);
            var registrationMethod = entry.IsSchedule ? "RegisterSchedule" : "RegisterExecute";
            writer.WriteLine(
                $"JobMiddlewareRegistry.{registrationMethod}({_Literal(entry.Identity)}, {function}, {entry.Priority}, static (context, next, cancellationToken) => context.Services.GetRequiredService<{entry.TypeName}>().InvokeAsync(context, next, cancellationToken));"
            );
        }

        writer.CloseBlock();
    }

    private static void _WriteFunctionRegistration(
        SourceWriter writer,
        JobFunctionModel function,
        JobsRegistrationModel model
    )
    {
        // Only async when the body awaits: the typed-context conversion or an awaitable method.
        var asyncFlag = function.UsesGenericContext || function.IsAwaitable ? "async " : "";
        var cronExpression = string.IsNullOrEmpty(function.CronExpression)
            ? "string.Empty"
            : $"\"{function.CronExpression}\"";

        // Bind to the named JobFunctionRegistration record rather than a positional tuple so new per-function knobs
        // stay additive for already-generated code.
        writer.WriteLine(
            $"jobFunctionDelegateDict.Add({_Literal(function.FunctionName)}, new JobFunctionRegistration {{ CronExpression = {cronExpression}, Priority = (JobPriority){function.Priority}, Delegate = new JobFunctionDelegate({asyncFlag}(serviceProvider, context, cancellationToken) =>"
        );
        writer.OpenBlock();

        if (function.UsesGenericContext)
        {
            writer.WriteLine(
                $"var genericContext = await ToGenericContextWithRequest<{_RequestTypeName(function.GenericTypeName, model)}>(context, cancellationToken);"
            );
        }

        var call =
            $"{_Receiver(function, model.AssemblyName)}.{function.MethodName}({string.Join(", ", function.InvocationArguments)})";
        if (function.IsAwaitable)
        {
            writer.WriteLine($"await {call};");
        }
        else
        {
            writer.WriteLine($"{call};");
            writer.WriteLine("return Task.CompletedTask;");
        }

        writer.CloseBlock($"), MaxConcurrency = {function.MaxConcurrency}{_RecoveryKnobs(function)} }});");
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

    /// <summary>
    /// The call receiver: the class for static methods, otherwise the generated factory. Classes in the assembly's root
    /// namespace use their simple name because the header imports that namespace.
    /// </summary>
    private static string _Receiver(JobFunctionModel function, string assemblyName)
    {
        var jobClass = function.Class;
        if (!function.IsStaticMethod)
        {
            return $"{_FactoryMethodName(jobClass)}(serviceProvider)";
        }

        return string.Equals(jobClass.Namespace, assemblyName, StringComparison.Ordinal)
            ? jobClass.Name
            : jobClass.FullName;
    }

    private static void _WriteDescriptorRegistration(SourceWriter writer, EquatableArray<JobFunctionModel> functions)
    {
        writer.WriteLine("private static void RegisterDescriptors()");
        writer.OpenBlock();

        if (functions.Count > 0)
        {
            writer.WriteLine($"var descriptors = new Dictionary<string, JobFunctionDescriptor>({functions.Count});");
            foreach (var function in _OrderedByName(functions))
            {
                var functionName = _Literal(function.FunctionName);
                var value = function.RequestTypeName is null
                    ? $"AppJobs.{_GetHandleName(function.FunctionName ?? string.Empty)}"
                    : $"new JobFunctionDescriptor({functionName}, typeof({function.RequestTypeName}), {_Literal(function.CronExpression ?? string.Empty)}, (JobPriority){function.Priority}, {function.MaxConcurrency}, {_Literal(function.ContractVersion)})";
                writer.WriteLine($"descriptors.Add({functionName}, {value});");
            }

            writer.WriteLine($"JobFunctionProvider.RegisterDescriptors(descriptors, {functions.Count});");
        }

        writer.CloseBlock();
    }

    private static void _WriteFactoryMethod(SourceWriter writer, JobClassModel jobClass, string assemblyName)
    {
        var className = string.Equals(jobClass.Namespace, assemblyName, StringComparison.Ordinal)
            ? jobClass.Name
            : jobClass.FullName;

        writer.WriteLine(
            $"private static {className} {_FactoryMethodName(jobClass)}(IServiceProvider serviceProvider)"
        );
        writer.OpenBlock();
        foreach (var parameter in jobClass.ConstructorParameters)
        {
            if (parameter.TypeName is null)
            {
                continue;
            }

            writer.WriteLine(
                parameter.ServiceKey is null
                    ? $"var {parameter.Name} = serviceProvider.GetService<{parameter.TypeName}>();"
                    : $"var {parameter.Name} = serviceProvider.GetKeyedService<{parameter.TypeName}>({parameter.ServiceKey});"
            );
        }

        writer.WriteLine(
            $"return new {className}({string.Join(", ", jobClass.ConstructorParameters.Select(x => x.Name))});"
        );
        writer.CloseBlock();
    }

    private static void _WriteGenericContextHelper(SourceWriter writer)
    {
        writer.WriteLine(
            "private static async Task<JobFunctionContext<T>> ToGenericContextWithRequest<T>(JobFunctionContext context, CancellationToken cancellationToken)"
        );
        writer.OpenBlock();
        writer.WriteLine("var request = await JobsRequestProvider.GetRequestAsync<T>(context, cancellationToken);");
        writer.WriteLine("return new JobFunctionContext<T>(context, request);");
        writer.CloseBlock();
    }

    private static void _WriteRequestTypeRegistration(SourceWriter writer, JobsRegistrationModel model)
    {
        var typedFunctions = model.Functions.Where(function => function.UsesGenericContext).ToList();

        writer.WriteLine("private static void RegisterRequestTypes()");
        writer.OpenBlock();

        if (typedFunctions.Count > 0)
        {
            writer.WriteLine($"var requestTypes = new Dictionary<string, (string, Type)>({typedFunctions.Count});");
            foreach (var function in typedFunctions)
            {
                var typeName = _RequestTypeName(function.GenericTypeName, model);
                writer.WriteLine(
                    $"requestTypes.Add(\"{function.FunctionName}\", (typeof({typeName}).FullName, typeof({typeName})));"
                );
            }

            writer.WriteLine($"JobFunctionProvider.RegisterRequestType(requestTypes, {typedFunctions.Count});");
        }

        writer.CloseBlock();
    }

    private static void _WriteAppJobs(
        SourceWriter writer,
        EquatableArray<JobFunctionModel> functions,
        string assemblyName
    )
    {
        var requestless = _OrderedByName(functions).Where(function => function.RequestTypeName is null).ToList();
        if (requestless.Count == 0)
        {
            return;
        }

        writer.WriteLine();
        writer.WriteLine($"namespace {assemblyName}");
        writer.OpenBlock();
        writer.WriteLine("/// <summary>Canonical generated handles for this assembly's requestless jobs.</summary>");
        writer.WriteLine("public static class AppJobs");
        writer.OpenBlock();
        foreach (var function in requestless)
        {
            var functionName = function.FunctionName ?? string.Empty;
            writer.WriteLine("/// <summary>A canonical requestless job descriptor.</summary>");
            writer.WriteLine(
                $"public static JobFunctionDescriptor {_GetHandleName(functionName)} {{ get; }} = new JobFunctionDescriptor({_Literal(functionName)}, null, {_Literal(function.CronExpression ?? string.Empty)}, (JobPriority){function.Priority}, {function.MaxConcurrency}, {_Literal(function.ContractVersion)});"
            );
        }

        writer.CloseBlock();
        writer.CloseBlock();
    }

    /// <summary>
    /// The request type as written in generated code: the simple name when some other request type shares a simple
    /// name, the display name when no names conflict at all.
    /// </summary>
    private static string _RequestTypeName(string fullTypeName, JobsRegistrationModel model)
    {
        if (model.ConflictingTypeNames.Count == 0)
        {
            return fullTypeName;
        }

        var simpleName = JobsRegistrationBuilder.SimpleName(fullTypeName);
        return model.ConflictingTypeNames.Contains(simpleName, StringComparer.Ordinal) ? fullTypeName : simpleName;
    }

    /// <summary>Distinct constructed classes in first-use order; static classes need no factory.</summary>
    private static IEnumerable<JobClassModel> _ConstructedClasses(EquatableArray<JobFunctionModel> functions)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in functions)
        {
            if (!function.Class.IsStatic && seen.Add(function.Class.FullName))
            {
                yield return function.Class;
            }
        }
    }

    private static string _FactoryMethodName(JobClassModel jobClass) => $"Create{jobClass.FullName.Replace(".", "")}";

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
