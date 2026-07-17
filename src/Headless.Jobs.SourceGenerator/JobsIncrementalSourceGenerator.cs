// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using System.Text;
using Headless.Jobs.SourceGenerator.AttributeSyntaxes;
using Headless.Jobs.SourceGenerator.Generators;
using Headless.Jobs.SourceGenerator.Models;
using Headless.Jobs.SourceGenerator.Utilities;
using Headless.Jobs.SourceGenerator.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Headless.Jobs.SourceGenerator;

#pragma warning disable MA0028, MA0076, RCS1213 // Generated source snippets and retained legacy helpers are clearer as-is.

/// <summary>
/// Roslyn incremental source generator that discovers methods annotated with
/// <c>[JobFunction]</c> and emits a per-assembly <c>JobsInstanceFactoryExtensions</c> class
/// containing a <c>[ModuleInitializer]</c> that registers each function with
/// <c>JobFunctionProvider</c> at process startup.
/// </summary>
/// <remarks>
/// The generated <c>Initialize()</c> method calls <c>JobFunctionProvider.RegisterFunctions</c>,
/// <c>JobFunctionProvider.RegisterRequestType</c>, and <c>JobFunctionProvider.RegisterDescriptors</c>
/// once per assembly, keyed by the function name declared on <c>[JobFunction]</c>. HF010 is emitted
/// for multiple <c>[JobsConstructor]</c> attributes, and HF011 rejects duplicate request types.
/// </remarks>
[Generator]
public sealed class JobsIncrementalSourceGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Registers the incremental pipeline that discovers <c>[JobFunction]</c> methods and emits
    /// the registration source.
    /// </summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var attributedMethods = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: (ctx, _) => _GetAttributedMethodIfAny(ctx)
            )
            .Where(pair => pair is not null)
            .Select((pair, _) => pair!.Value);

        var compilationAndMethods = context.CompilationProvider.Combine(attributedMethods.Collect());

        context.RegisterSourceOutput(
            compilationAndMethods,
            (productionContext, source) =>
            {
                var (compilation, methodInfos) = source;

                if (string.Equals(compilation.Assembly.Name, "Jobs", StringComparison.Ordinal))
                {
                    return;
                }

                var jobMethodPairs = methodInfos
                    .Where(info => info.HasJobFunction)
                    .Select(info => (info.ClassDecl, info.MethodDecl))
                    .ToImmutableArray();
                var methodPairs = methodInfos.Select(info => (info.ClassDecl, info.MethodDecl)).ToImmutableArray();

                if (!CompilationCollisionValidator.Validate(jobMethodPairs, compilation, productionContext))
                {
                    return;
                }

                // Generate constructor calls (no need for class conflict detection since we always use full names)
                var constructorCalls = _BuildConstructorMethodCalls(
                        jobMethodPairs,
                        compilation,
                        compilation.Assembly.Name
                    )
                    .ToList();

                // Generate delegates and detect type conflicts for generic types
                var initialDelegatesWithMetadata = _BuildJobFunctionDelegates(
                        jobMethodPairs,
                        compilation,
                        productionContext,
                        compilation.Assembly.Name
                    )
                    .ToList();
                var typeNames = initialDelegatesWithMetadata
                    .Select(info => info.RequestType.GenericTypeName)
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();
                var typeNameConflicts = _DetectTypeNameConflicts(typeNames);

                // Regenerate delegates with type conflict information for generic types
                var delegatesWithMetadata = _BuildJobFunctionDelegates(
                        jobMethodPairs,
                        compilation,
                        productionContext,
                        compilation.Assembly.Name,
                        typeNameConflicts
                    )
                    .ToList();

                var delegateCodes = delegatesWithMetadata.ConvertAll(info => info.DelegateCode);
                var requestTypes = delegatesWithMetadata.ConvertAll(info => info.RequestType);
                var descriptors = delegatesWithMetadata.ConvertAll(info => info.Descriptor);
                var middleware = MiddlewareRegistrationCollector
                    .Collect(compilation, descriptors.Select(x => x.FunctionName), methodPairs, productionContext)
                    .ToList();

                var generatedCode = _GenerateSourceWithFullNamespaces(
                    delegateCodes,
                    constructorCalls,
                    requestTypes,
                    descriptors,
                    middleware,
                    compilation.Assembly.Name,
                    typeNameConflicts
                );

                productionContext.AddSource(
                    SourceGeneratorConstants.GeneratedFileName,
                    SourceText.From(SourceGeneratorUtilities.FormatCode(generatedCode), Encoding.UTF8)
                );
            }
        );
    }

    /// <summary>
    /// Extracts attributed method information from the current compilation.
    /// </summary>
    private static (
        ClassDeclarationSyntax ClassDecl,
        MethodDeclarationSyntax MethodDecl,
        bool HasJobFunction
    )? _GetAttributedMethodIfAny(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not MethodDeclarationSyntax methodSyntax)
        {
            return null;
        }

        var semanticModel = ctx.SemanticModel;
        if (semanticModel.GetDeclaredSymbol(methodSyntax) is not { } methodSymbol)
        {
            return null;
        }

        if (
            !string.Equals(
                methodSymbol.ContainingAssembly.Name,
                semanticModel.Compilation.Assembly.Name,
                StringComparison.Ordinal
            )
        )
        {
            return null;
        }

        if (methodSyntax.Parent is not ClassDeclarationSyntax cd)
        {
            return null;
        }

        var attributes = methodSymbol.GetAttributes();
        var hasJobFunction = attributes.Any(_IsJobFunctionAttribute);
        var hasMiddleware = attributes.Any(MiddlewareRegistrationCollector.IsMiddlewareAttribute);
        if (!hasJobFunction && !hasMiddleware)
        {
            return null;
        }

        return (cd, methodSyntax, hasJobFunction);
    }

    /// <summary>
    /// Builds job function delegates for all discovered methods with JobFunction attributes.
    /// </summary>
    private static IEnumerable<JobFunctionGenerationInfo> _BuildJobFunctionDelegates(
        ImmutableArray<(ClassDeclarationSyntax ClassDecl, MethodDeclarationSyntax MethodDecl)> methodPairs,
        Compilation compilation,
        SourceProductionContext context,
        string? assemblyName = null,
        HashSet<string>? typeNameConflicts = null
    )
    {
        var validatedClasses = new HashSet<ClassDeclarationSyntax>();

        foreach (var (classDeclaration, methodDeclaration) in methodPairs)
        {
            JobFunctionValidator.ValidateClassAndMethod(classDeclaration, methodDeclaration, compilation, context);

            var semanticModel = compilation.GetSemanticModel(methodDeclaration.SyntaxTree);

            // Validate multiple constructors once per class
            if (validatedClasses.Add(classDeclaration))
            {
                ConstructorValidator.ValidateMultipleConstructors(classDeclaration, semanticModel, context);
                JobFunctionValidator.ValidateNotNestedClass(classDeclaration, context);
            }
            var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);

            var jobFunctionAttributeData = methodSymbol?.GetAttributes().FirstOrDefault(_IsJobFunctionAttribute);
            if (jobFunctionAttributeData == null)
            {
                continue;
            }

            var attributeValues = jobFunctionAttributeData.GetJobFunctionAttributeValues();
#pragma warning disable MA0045 // Incremental generator transform is synchronous at this point.
            var attributeLocation =
                jobFunctionAttributeData.ApplicationSyntaxReference?.GetSyntax()?.GetLocation()
                ?? methodDeclaration.Identifier.GetLocation();
#pragma warning restore MA0045

            // Validate all attribute values
            AttributeValidator.ValidateJobFunctionAttribute(
                attributeValues,
                methodDeclaration,
                classDeclaration.Identifier.Text,
                attributeLocation,
                context
            );

            // Validate method parameters
            JobFunctionValidator.ValidateMethodParameters(methodDeclaration, methodSymbol, context);

            yield return _BuildSingleDelegate(
                classDeclaration,
                methodDeclaration,
                semanticModel,
                attributeValues.functionName,
                attributeValues.taskPriority,
                attributeValues.maxConcurrency,
                attributeValues.cronExpression,
                assemblyName ?? compilation.Assembly.Name,
                typeNameConflicts
            );
        }
    }

    /// <summary>
    /// Builds a single delegate for a job function method.
    /// </summary>
    private static JobFunctionGenerationInfo _BuildSingleDelegate(
        ClassDeclarationSyntax classDeclaration,
        MethodDeclarationSyntax methodDeclaration,
        SemanticModel semanticModel,
        string? functionName,
        int functionPriority,
        int maxConcurrency,
        string? cronExpression,
        string assemblyName,
        HashSet<string>? typeNameConflicts = null
    )
    {
        var methodInfo = DelegateGenerator.AnalyzeMethodParameters(methodDeclaration, semanticModel);
        var isAwaitable = SourceGeneratorUtilities.IsMethodAwaitable(methodDeclaration);

        var delegateCode = DelegateGenerator.GenerateDelegateCode(
            classDeclaration,
            methodDeclaration,
            methodInfo,
            isAwaitable,
            functionName!,
            functionPriority,
            maxConcurrency,
            cronExpression!,
            assemblyName,
            typeNameConflicts
        );

        var requestType = CompilationCollisionValidator.GetRequestType(
            semanticModel.GetDeclaredSymbol(methodDeclaration)
        );

        return new(
            delegateCode,
            (methodInfo.GenericTypeName, functionName!),
            new(
                functionName!,
                requestType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                cronExpression ?? string.Empty,
                functionPriority,
                maxConcurrency
            )
        );
    }

    /// <summary>
    /// Generates the complete source code for the factory class using full namespaces.
    /// This approach eliminates all using statement complexity and ensures reliable compilation.
    /// </summary>
    private static string _GenerateSourceWithFullNamespaces(
        IReadOnlyList<string> delegates,
        IReadOnlyList<string> ctorCalls,
        IReadOnlyList<(string GenericTypeName, string FunctionName)> requestTypes,
        IReadOnlyList<JobFunctionDescriptorInfo> descriptors,
        IReadOnlyList<MiddlewareRegistrationInfo> middleware,
        string assemblyName,
        HashSet<string>? typeNameConflicts = null
    )
    {
        var sb = new StringBuilder(SourceGeneratorConstants.InitialStringBuilderCapacity);

        // Check if ToGenericContextWithRequest is used (if any request types exist)
        var includeBaseUtilities = requestTypes.Any(rt => !string.IsNullOrEmpty(rt.GenericTypeName));

        _GenerateFileHeaderWithJobsUsings(sb, includeBaseUtilities, assemblyName);
        _GenerateDescriptorMetadata(sb, descriptors);
        _GenerateClassDeclarationWithFullNamespaces(sb, assemblyName);
        _GenerateInitializeMethodWithFullNamespaces(sb, delegates, middleware);
        _GenerateDescriptorRegistrationWithFullNamespaces(sb, descriptors);
        _GenerateConstructorMethods(sb, ctorCalls); // Constructor methods already handle their own namespacing

        // Only generate helper method if it's needed
        if (includeBaseUtilities)
        {
            _GenerateHelperMethodsWithFullNamespaces(sb);
        }

        _GenerateRequestTypeRegistrationWithFullNamespaces(sb, requestTypes, typeNameConflicts);
        _GenerateClassFooter(sb);

        return sb.ToString();
    }

    private static void _GenerateDescriptorMetadata(
        StringBuilder sb,
        IReadOnlyList<JobFunctionDescriptorInfo> descriptors
    )
    {
        foreach (var descriptor in descriptors.OrderBy(x => x.FunctionName, StringComparer.Ordinal))
        {
            sb.AppendLine(
                $"[assembly: global::Headless.Jobs.JobFunctionDescriptorMetadataAttribute(\"{descriptor.FunctionName}\")]"
            );
        }

        if (descriptors.Count > 0)
        {
            sb.AppendLine();
        }
    }

    private static bool _IsJobFunctionAttribute(AttributeData attribute) =>
        string.Equals(
            attribute.AttributeClass?.Name,
            SourceGeneratorConstants.JobFunctionAttributeName,
            StringComparison.Ordinal
        );

    /// <summary>
    /// Generates the complete source code for the factory class (legacy method with using statements).
    /// </summary>
    private static string _GenerateSource(
        IReadOnlyList<string> delegates,
        IReadOnlyList<string> ctorCalls,
        IReadOnlyList<(string GenericTypeName, string FunctionName)> requestTypes,
        string assemblyName,
        HashSet<string>? additionalNamespaces = null,
        HashSet<string>? typeNameConflicts = null
    )
    {
        var sb = new StringBuilder(SourceGeneratorConstants.InitialStringBuilderCapacity);

        // No type aliases needed - we'll use full names when conflicts exist

        // Collect all required namespaces from the generated content
        var requiredNamespaces = NamespaceCollector.CollectRequiredNamespaces(
            delegates,
            ctorCalls,
            requestTypes,
            assemblyName,
            additionalNamespaces
        );

        _GenerateFileHeader(sb, requiredNamespaces);
        _GenerateClassDeclaration(sb, assemblyName);
        _GenerateInitializeMethod(sb, delegates);
        _GenerateConstructorMethods(sb, ctorCalls);
        _GenerateHelperMethods(sb);
        _GenerateRequestTypeRegistration(sb, requestTypes, typeNameConflicts);
        _GenerateClassFooter(sb);

        return sb.ToString();
    }

    /// <summary>
    /// Extracts the class name from a constructor call method string.
    /// </summary>
    private static string _ExtractClassNameFromConstructorCall(string constructorCall)
    {
        if (string.IsNullOrEmpty(constructorCall))
        {
            return string.Empty;
        }

        // Look for pattern: "private static ClassName Create..."
        var lines = constructorCall.Split('\n');
        var methodLine = lines.FirstOrDefault(l => l.Contains("private static") && l.Contains("Create"));
        if (methodLine == null)
        {
            return string.Empty;
        }

        // Extract the return type (class name) between "private static " and " Create"
        var startIndex = methodLine.IndexOf("private static ", StringComparison.Ordinal) + "private static ".Length;
        var endIndex = methodLine.IndexOf(" Create", StringComparison.Ordinal);
        if (startIndex >= 0 && endIndex > startIndex)
        {
            return methodLine.Substring(startIndex, endIndex - startIndex).Trim();
        }

        return string.Empty;
    }

    /// <summary>
    /// Detects class name conflicts and returns a set of simple class names that have conflicts.
    /// </summary>
    private static HashSet<string> _DetectClassNameConflicts(List<string> fullClassNames)
    {
        var simpleNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        // Count occurrences of each simple class name
        foreach (var fullClassName in fullClassNames)
        {
            var simpleName = fullClassName.Contains('.')
                ? fullClassName.Substring(fullClassName.LastIndexOf('.') + 1)
                : fullClassName;

            simpleNameCounts[simpleName] = simpleNameCounts.TryGetValue(simpleName, out var count) ? count + 1 : 1;
        }

        // Return simple names that have conflicts (count > 1)
        return new HashSet<string>(
            simpleNameCounts.Where(kv => kv.Value > 1).Select(kv => kv.Key),
            StringComparer.Ordinal
        );
    }

    /// <summary>
    /// Detects type name conflicts and returns a set of simple type names that have conflicts.
    /// </summary>
    private static HashSet<string> _DetectTypeNameConflicts(List<string> fullTypeNames)
    {
        var simpleNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        // Count occurrences of each simple type name
        foreach (var fullTypeName in fullTypeNames)
        {
            if (string.IsNullOrEmpty(fullTypeName))
            {
                continue;
            }

            var simpleName = fullTypeName.Contains('.')
                ? fullTypeName.Substring(fullTypeName.LastIndexOf('.') + 1)
                : fullTypeName;

            simpleNameCounts[simpleName] = simpleNameCounts.TryGetValue(simpleName, out var count) ? count + 1 : 1;
        }

        // Return simple names that have conflicts (count > 1)
        return new HashSet<string>(
            simpleNameCounts.Where(kv => kv.Value > 1).Select(kv => kv.Key),
            StringComparer.Ordinal
        );
    }

    /// <summary>
    /// Generates the file header with Jobs and common .NET using statements.
    /// </summary>
    private static void _GenerateFileHeaderWithJobsUsings(
        StringBuilder sb,
        bool includeBaseUtilities = false,
        string? assemblyName = null
    )
    {
        sb.AppendLine("//Jobs readonly auto-generated file.");
        sb.AppendLine("#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member");

        // Include common .NET using statements
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");

        // Include Jobs using statements
        sb.AppendLine("using Headless.Jobs;");
        sb.AppendLine("using Headless.Jobs.Enums;");

        // Include Base namespace if ToGenericContextWithRequest is used
        if (includeBaseUtilities)
        {
            sb.AppendLine("using Headless.Jobs.Base;");
        }

        // Include root namespace (assembly name) as using statement
        if (!string.IsNullOrEmpty(assemblyName))
        {
            sb.AppendLine($"using {assemblyName};");
        }

        sb.AppendLine();
    }

    /// <summary>
    /// Generates the file header with using statements (legacy method).
    /// </summary>
    private static void _GenerateFileHeader(StringBuilder sb, HashSet<string> requiredNamespaces)
    {
        sb.AppendLine("//Jobs readonly auto-generated file.");
        sb.AppendLine("#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member");

        // Sort namespaces for consistent output
        var sortedNamespaces = requiredNamespaces.OrderBy(ns => ns, StringComparer.Ordinal);
        foreach (var ns in sortedNamespaces)
        {
            sb.AppendLine($"using {ns};");
        }

        sb.AppendLine();
    }

    #region Code Generation Methods

    /// <summary>
    /// Generates the class declaration.
    /// </summary>
    /// <summary>
    /// Generates the class declaration with full namespaces.
    /// </summary>
    private static void _GenerateClassDeclarationWithFullNamespaces(StringBuilder sb, string assemblyName)
    {
        sb.AppendLine($"namespace {assemblyName}");
        sb.AppendLine("{");
        // Internal: this registration class is invoked only by its own [ModuleInitializer], so it never needs to
        // appear on the consuming assembly's public surface.
        sb.AppendLine("    internal static class JobsInstanceFactoryExtensions");
        sb.AppendLine("    {");
    }

    private static void _GenerateClassDeclaration(StringBuilder sb, string assemblyName)
    {
        sb.AppendLine($"namespace {assemblyName}");
        sb.AppendLine("{");
        // Internal: this registration class is invoked only by its own [ModuleInitializer], so it never needs to
        // appear on the consuming assembly's public surface.
        sb.AppendLine("  internal static class JobsInstanceFactoryExtensions");
        sb.AppendLine("  {");
    }

    /// <summary>
    /// Generates the Initialize method with delegate registrations.
    /// </summary>
    /// <summary>
    /// Generates the Initialize method with delegate registrations using full namespaces.
    /// </summary>
    private static void _GenerateInitializeMethodWithFullNamespaces(
        StringBuilder sb,
        IEnumerable<string> delegates,
        IReadOnlyList<MiddlewareRegistrationInfo> middleware
    )
    {
        var delegateList = delegates.ToList();
        var delegateCount = delegateList.Count;

        sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("        public static void Initialize()");
        sb.AppendLine("        {");

        if (delegateCount > 0)
        {
            sb.AppendLine(
                $"            var jobFunctionDelegateDict = new Dictionary<string, JobFunctionRegistration>({delegateCount});"
            );

            foreach (var delegateCode in delegateList)
            {
                sb.Append(delegateCode);
            }

            sb.AppendLine(
                $"            JobFunctionProvider.RegisterFunctions(jobFunctionDelegateDict, {delegateCount});"
            );
        }

        sb.AppendLine("            RegisterRequestTypes();");
        sb.AppendLine("            RegisterDescriptors();");
        foreach (var entry in middleware)
        {
            var identity = SymbolDisplay.FormatLiteral(entry.Identity, quote: true);
            var function = entry.Function is null ? "null" : SymbolDisplay.FormatLiteral(entry.Function, quote: true);
            var registrationMethod = entry.IsSchedule ? "RegisterSchedule" : "RegisterExecute";
            sb.AppendLine(
                $"            JobMiddlewareRegistry.{registrationMethod}({identity}, {function}, {entry.Priority}, static (context, next, cancellationToken) => context.Services.GetRequiredService<{entry.TypeName}>().InvokeAsync(context, next, cancellationToken));"
            );
        }
        sb.AppendLine("        }");
    }

    private static void _GenerateDescriptorRegistrationWithFullNamespaces(
        StringBuilder sb,
        IReadOnlyList<JobFunctionDescriptorInfo> descriptors
    )
    {
        sb.AppendLine("        private static void RegisterDescriptors()");
        sb.AppendLine("        {");

        if (descriptors.Count > 0)
        {
            sb.AppendLine(
                $"            var descriptors = new Dictionary<string, JobFunctionDescriptor>({descriptors.Count});"
            );

            foreach (var descriptor in descriptors)
            {
                var functionName = SymbolDisplay.FormatLiteral(descriptor.FunctionName, quote: true);
                var requestType = descriptor.RequestTypeName == null ? "null" : $"typeof({descriptor.RequestTypeName})";
                var cronExpression = SymbolDisplay.FormatLiteral(descriptor.CronExpression, quote: true);
                sb.AppendLine(
                    $"            descriptors.Add({functionName}, new JobFunctionDescriptor({functionName}, {requestType}, {cronExpression}, (JobPriority){descriptor.Priority}, {descriptor.MaxConcurrency}));"
                );
            }

            sb.AppendLine($"            JobFunctionProvider.RegisterDescriptors(descriptors, {descriptors.Count});");
        }

        sb.AppendLine("        }");
    }

    private static void _GenerateInitializeMethod(StringBuilder sb, IEnumerable<string> delegates)
    {
        var delegateList = delegates.ToList();
        var delegateCount = delegateList.Count;

        sb.AppendLine("[global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("    public static void Initialize()");
        sb.AppendLine("    {");

        if (delegateCount > 0)
        {
            sb.AppendLine(
                $"      var jobFunctionDelegateDict = new Dictionary<string, JobFunctionRegistration>({delegateCount});"
            );

            foreach (var delegateCode in delegateList)
            {
                sb.Append(delegateCode);
            }

            sb.AppendLine($"      JobFunctionProvider.RegisterFunctions(jobFunctionDelegateDict, {delegateCount});");
        }

        sb.AppendLine("      RegisterRequestTypes();");
        sb.AppendLine("    }");
    }

    /// <summary>
    /// Generates constructor methods for dependency injection.
    /// </summary>
    private static void _GenerateConstructorMethods(StringBuilder sb, IEnumerable<string> ctorCalls)
    {
        foreach (var ctorCall in ctorCalls)
        {
            sb.AppendLine(ctorCall);
        }
    }

    /// <summary>
    /// Generates helper methods for generic context handling using full namespaces.
    /// </summary>
    private static void _GenerateHelperMethodsWithFullNamespaces(StringBuilder sb)
    {
        sb.AppendLine("        private static async Task<JobFunctionContext<T>> ToGenericContextWithRequest<T>(");
        sb.AppendLine("            JobFunctionContext context,");
        sb.AppendLine("            CancellationToken cancellationToken)");
        sb.AppendLine("        {");
        sb.AppendLine(
            "            var request = await JobsRequestProvider.GetRequestAsync<T>(context, cancellationToken);"
        );
        sb.AppendLine("            return new JobFunctionContext<T>(context, request);");
        sb.AppendLine("        }");
    }

    /// <summary>
    /// Generates helper methods for generic context handling (legacy method).
    /// </summary>
    private static void _GenerateHelperMethods(StringBuilder sb)
    {
        sb.AppendLine("    private static async Task<JobFunctionContext<T>> ToGenericContextWithRequest<T>(");
        sb.AppendLine("      JobFunctionContext context,");
        sb.AppendLine("      CancellationToken cancellationToken)");
        sb.AppendLine("    {");
        sb.AppendLine("      var request = await JobsRequestProvider.GetRequestAsync<T>(context, cancellationToken);");
        sb.AppendLine("      return new JobFunctionContext<T>(context, request);");
        sb.AppendLine("    }");
    }

    /// <summary>
    /// Generates the request type registration method.
    /// </summary>
    /// <summary>
    /// Generates the request type registration method using full namespaces.
    /// </summary>
    private static void _GenerateRequestTypeRegistrationWithFullNamespaces(
        StringBuilder sb,
        IEnumerable<(string GenericTypeName, string FunctionName)> requestTypes,
        HashSet<string>? typeNameConflicts = null
    )
    {
        var requestTypesList = requestTypes.ToList();
        var requestTypesWithGeneric = requestTypesList.Where(rt => !string.IsNullOrEmpty(rt.GenericTypeName)).ToList();
        var requestTypesCount = requestTypesWithGeneric.Count;

        sb.AppendLine("        private static void RegisterRequestTypes()");
        sb.AppendLine("        {");

        if (requestTypesCount > 0)
        {
            sb.AppendLine(
                $"            var requestTypes = new Dictionary<string, (string, Type)>({requestTypesCount});"
            );

            foreach (var (genericTypeName, functionName) in requestTypesWithGeneric)
            {
                // Use the simple type name if no conflicts exist, otherwise use full name
                var typeName = _GetTypeNameForGeneration(genericTypeName, typeNameConflicts);

                sb.AppendLine(
                    $"            requestTypes.Add(\"{functionName}\", (typeof({typeName}).FullName, typeof({typeName})));"
                );
            }

            sb.AppendLine($"            JobFunctionProvider.RegisterRequestType(requestTypes, {requestTypesCount});");
        }

        sb.AppendLine("        }");
    }

    private static void _GenerateRequestTypeRegistration(
        StringBuilder sb,
        IEnumerable<(string GenericTypeName, string FunctionName)> requestTypes,
        HashSet<string>? typeNameConflicts = null
    )
    {
        var requestTypesList = requestTypes.ToList();
        var requestTypesWithGeneric = requestTypesList.Where(rt => !string.IsNullOrEmpty(rt.GenericTypeName)).ToList();
        var requestTypesCount = requestTypesWithGeneric.Count;

        sb.AppendLine("    private static void RegisterRequestTypes()");
        sb.AppendLine("    {");

        if (requestTypesCount > 0)
        {
            sb.AppendLine($"      var requestTypes = new Dictionary<string, (string, Type)>({requestTypesCount});");

            foreach (var (genericTypeName, functionName) in requestTypesWithGeneric)
            {
                // Use the simple type name if no conflicts exist, otherwise use full name
                var typeName = _GetTypeNameForGeneration(genericTypeName, typeNameConflicts);

                sb.AppendLine(
                    $"      requestTypes.Add(\"{functionName}\", (typeof({typeName}).FullName, typeof({typeName})));"
                );
            }

            sb.AppendLine($"      JobFunctionProvider.RegisterRequestType(requestTypes, {requestTypesCount});");
        }

        sb.AppendLine("    }");
    }

    /// <summary>
    /// Gets the appropriate type name for code generation - simple name if no conflicts, full name if conflicts exist.
    /// </summary>
    private static string _GetTypeNameForGeneration(string fullTypeName, HashSet<string>? typeNameConflicts)
    {
        if (typeNameConflicts == null || typeNameConflicts.Count == 0)
        {
            return fullTypeName;
        }

        var simpleName = fullTypeName.Contains('.')
            ? fullTypeName.Substring(fullTypeName.LastIndexOf('.') + 1)
            : fullTypeName;

        // Use full name if there's a conflict with the simple name
        return typeNameConflicts.Contains(simpleName) ? fullTypeName : simpleName;
    }

    /// <summary>
    /// Generates the class closing braces.
    /// </summary>
    private static void _GenerateClassFooter(StringBuilder sb)
    {
        sb.AppendLine("  }");
        sb.AppendLine("}");
    }

    #endregion

    #region Constructor Building Methods

    /// <summary>
    /// Builds constructor method calls for dependency injection.
    /// </summary>
    private static IEnumerable<string> _BuildConstructorMethodCalls(
        IEnumerable<(ClassDeclarationSyntax ClassDecl, MethodDeclarationSyntax MethodDecl)> methodPairs,
        Compilation compilation,
        string assemblyName
    )
    {
        var distinctClasses = methodPairs.Select(p => p.ClassDecl).Distinct();
        foreach (var classDeclaration in distinctClasses)
        {
            var semanticModel = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
            var constructorCall = _BuildConstructorCall(classDeclaration, semanticModel, assemblyName);

            if (!string.IsNullOrEmpty(constructorCall))
            {
                yield return constructorCall!;
            }
        }
    }

    /// <summary>
    /// Builds a constructor call method for a specific class.
    /// Prioritizes constructors with [JobsConstructor] attribute, then first public constructor.
    /// Skips static classes as they cannot be instantiated.
    /// </summary>
    private static string? _BuildConstructorCall(
        ClassDeclarationSyntax classDeclaration,
        SemanticModel semanticModel,
        string assemblyName
    )
    {
        // Check if class is static - static classes cannot be instantiated
        var isStaticClass = classDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
        if (isStaticClass)
        {
            return null; // Skip constructor generation for static classes
        }

        var constructors = classDeclaration.Members.OfType<ConstructorDeclarationSyntax>().ToList();

        // First, look for a constructor with JobsConstructor attribute using semantic analysis
        var jobConstructor = constructors.Find(c =>
        {
            var constructorSymbol = semanticModel.GetDeclaredSymbol(c);
            if (constructorSymbol == null)
            {
                return false;
            }

            return constructorSymbol
                .GetAttributes()
                .Any(attr =>
                {
                    var attributeClass = attr.AttributeClass;
                    if (attributeClass == null)
                    {
                        return false;
                    }

                    var attributeName = attributeClass.Name;
                    var fullName = attributeClass.ToDisplayString();

                    return string.Equals(attributeName, "JobsConstructorAttribute", StringComparison.Ordinal)
                        || string.Equals(attributeName, "JobsConstructor", StringComparison.Ordinal)
                        || string.Equals(
                            fullName,
                            "Headless.Jobs.Base.JobsConstructorAttribute",
                            StringComparison.Ordinal
                        )
                        || string.Equals(fullName, "Headless.Jobs.Base.JobsConstructor", StringComparison.Ordinal);
                });
        });

        // If no JobsConstructor attribute found, use first public constructor
        var publicConstructor =
            jobConstructor ?? constructors.Find(c => c.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)));

        var isPrimaryConstructor = classDeclaration.ParameterList?.Parameters.Count > 0;
        var parameters = isPrimaryConstructor
            ? classDeclaration.ParameterList!.Parameters
            : publicConstructor?.ParameterList?.Parameters ?? default;

        var sb = new StringBuilder(512); // Constructor methods are typically smaller

        // Use simple class name if in root namespace (due to using statement), otherwise use full name
        var fullClassName = SourceGeneratorUtilities.GetFullClassName(classDeclaration);
        var classNamespace = SourceGeneratorUtilities.GetNamespace(classDeclaration);
        var simpleClassName = classDeclaration.Identifier.Text;

        // Use simple name if in the same namespace as assembly (root namespace)
        var useSimpleName = string.Equals(classNamespace, assemblyName, StringComparison.Ordinal);
        var displayClassName = useSimpleName ? simpleClassName : fullClassName;
        var methodName = $"Create{fullClassName.Replace(".", "")}";

        sb.AppendLine($"    private static {displayClassName} {methodName}(IServiceProvider serviceProvider)");
        sb.AppendLine("    {");

        var arguments = new List<string>();
        foreach (var parameter in parameters)
        {
            var parameterName = SourceGeneratorUtilities.FirstLetterToLower(parameter.Identifier.Text);
            if (!string.Equals(parameterName, "serviceProvider", StringComparison.Ordinal))
            {
                if (parameter.Type != null)
                {
                    // Get parameter symbol - handle both regular and primary constructors
                    var parameterSymbol = isPrimaryConstructor
                        ? _GetPrimaryConstructorParameterSymbol(classDeclaration, parameter, semanticModel)
                        : semanticModel.GetDeclaredSymbol(parameter);

                    var serviceResolution = _GenerateServiceResolution(
                        parameter,
                        parameterSymbol,
                        semanticModel,
                        parameterName
                    );
                    sb.AppendLine(serviceResolution);
                }
            }
            arguments.Add(parameterName);
        }

        sb.AppendLine($"        return new {displayClassName}({string.Join(", ", arguments)});");
        sb.AppendLine("    }");
        return sb.ToString();
    }

    /// <summary>
    /// Generates the appropriate service resolution code for a constructor parameter.
    /// </summary>
    private static string _GenerateServiceResolution(
        ParameterSyntax parameter,
        IParameterSymbol? parameterSymbol,
        SemanticModel semanticModel,
        string parameterName
    )
    {
        var typeSymbol = ModelExtensions.GetSymbolInfo(semanticModel, parameter.Type!).Symbol;
        var typeName = typeSymbol?.ToDisplayString() ?? parameter.Type!.ToString();

        // Check for FromKeyedServicesAttribute (with various possible names)
        var keyedServiceAttribute = parameterSymbol
            ?.GetAttributes()
            .FirstOrDefault(attr =>
            {
                var name = attr.AttributeClass?.Name;
                var fullName = attr.AttributeClass?.ToDisplayString();
                return string.Equals(
                        name,
                        SourceGeneratorConstants.FromKeyedServicesAttributeName,
                        StringComparison.Ordinal
                    )
                    || string.Equals(name, "FromKeyedServices", StringComparison.Ordinal)
                    || string.Equals(
                        fullName,
                        "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute",
                        StringComparison.Ordinal
                    )
                    || fullName?.EndsWith(
                        SourceGeneratorConstants.FromKeyedServicesAttributeName,
                        StringComparison.Ordinal
                    ) == true;
            });

        if (keyedServiceAttribute != null)
        {
            var serviceKey = SourceGeneratorUtilities.GetServiceKey(keyedServiceAttribute);
            if (serviceKey != null)
            {
                return $"        var {parameterName} = serviceProvider.GetKeyedService<{typeName}>({serviceKey});";
            }
        }

        // Default to regular service resolution
        return $"        var {parameterName} = serviceProvider.GetService<{typeName}>();";
    }

    /// <summary>
    /// Gets the parameter symbol for primary constructor parameters.
    /// </summary>
    private static IParameterSymbol? _GetPrimaryConstructorParameterSymbol(
        ClassDeclarationSyntax classDeclaration,
        ParameterSyntax parameter,
        SemanticModel semanticModel
    )
    {
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);

        if (classSymbol?.Constructors.Length > 0)
        {
            var primaryConstructor = classSymbol.Constructors.FirstOrDefault(c => c.Parameters.Length > 0);
            if (primaryConstructor != null)
            {
                var parameterName = parameter.Identifier.Text;
                return primaryConstructor.Parameters.FirstOrDefault(p =>
                    string.Equals(p.Name, parameterName, StringComparison.Ordinal)
                );
            }
        }
        return null;
    }

    #endregion

    // All utility methods moved to SourceGeneratorUtilities class
}

#pragma warning restore MA0028, MA0076, RCS1213
