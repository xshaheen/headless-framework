// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.CompilerServices;
using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Validators;
using Headless.OpenApi.Nswag.SchemaProcessors.FluentValidation;
using Headless.OpenApi.Nswag.SchemaProcessors.FluentValidation.Models;
using Headless.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Namotion.Reflection;
using NJsonSchema.Generation;

namespace Headless.OpenApi.Nswag.SchemaProcessors;

/// <summary>
/// NSwag schema processor that translates FluentValidation validator rules into OpenAPI schema
/// constraints (required, minLength, maxLength, pattern, minimum, maximum, etc.) instead of relying
/// on <c>System.ComponentModel</c> data annotations.
/// </summary>
/// <remarks>
/// <para>
/// The processor resolves a registered <c>IValidator&lt;T&gt;</c> from a fresh DI scope for each
/// schema type. Rules are merged with the built-in default rule set; callers may override or extend
/// individual rules by passing a <paramref name="rules"/> collection — rules with the same
/// <c>RuleName</c> replace the defaults.
/// </para>
/// <para>
/// Errors during rule application are logged and swallowed by default. Set
/// <see cref="HeadlessNswagOptions.ThrowOnSchemaProcessingError"/> to <see langword="true"/> to
/// re-throw errors during development.
/// </para>
/// <para>
/// Included validators (FluentValidation <c>Include()</c> / <c>RuleFor…SetValidator</c>) are followed
/// recursively so that base-class rules are also reflected in the schema.
/// </para>
/// </remarks>
/// <param name="serviceProvider">
/// The application service provider used to resolve <c>IValidator&lt;T&gt;</c> instances and an
/// optional <c>ILoggerFactory</c>.
/// </param>
/// <param name="options">
/// Optional Headless NSwag options. When <see langword="null"/>, default options are used
/// (<see cref="HeadlessNswagOptions.ThrowOnSchemaProcessingError"/> defaults to <see langword="false"/>).
/// </param>
/// <param name="rules">
/// Optional set of <see cref="FluentValidationRule"/> instances that replace or extend the default
/// rules. When <see langword="null"/>, <see cref="FluentValidationRule.DefaultRules"/> are used.
/// </param>
public sealed class FluentValidationSchemaProcessor(
    IServiceProvider serviceProvider,
    HeadlessNswagOptions? options = null,
    IEnumerable<FluentValidationRule>? rules = null
) : ISchemaProcessor
{
    private static readonly ConditionalWeakTable<Type, CachedResult<MethodInfo>> _MethodCache = [];

    private static readonly ConditionalWeakTable<
        Type,
        CachedResult<MethodInfo>
    >.CreateValueCallback _GetValidatorMethodFactory =
#pragma warning disable REFL017, REFL003 // Justification: Already of type ChildValidatorAdaptor<,>
    static t => new CachedResult<MethodInfo>(t.GetMethod(nameof(ChildValidatorAdaptor<,>.GetValidator)));
#pragma warning restore REFL017, REFL003

    private readonly ILogger _logger = _CreateLogger(serviceProvider);
    private readonly IReadOnlyList<FluentValidationRule> _rules = _CreateRules(rules);
    private readonly bool _throwOnError = options?.ThrowOnSchemaProcessingError ?? false;

    /// <summary>
    /// Applies FluentValidation rules to the schema for the type being processed.
    /// </summary>
    /// <param name="context">The NSwag schema processor context for the type being processed.</param>
    public void Process(SchemaProcessorContext context)
    {
        if (context.Schema is { IsObject: true, Properties.Count: > 0 })
        {
            _HandleObject(context);
        }
        else
        {
            // Not an object but is a property of an object type
            // (e.g. a class that used as a query parameters so it's not an object in swagger)
            // but we still have a validator for its declaring type so we can apply rules to it.
            _HandleProperty(context);
        }
    }

    private void _HandleObject(SchemaProcessorContext context)
    {
#pragma warning disable MA0045 // Justification: We are using a scope to resolve the validator, this is fine.
        using var scope = serviceProvider.CreateScope();
#pragma warning restore MA0045
        var validator = _GetValidator(scope.ServiceProvider, context.ContextualType);

        if (validator is null)
        {
            return;
        }

        _ApplyRulesToSchema(context, validator);

        try
        {
            _AddRulesFromIncludedValidators(context, validator);
        }
        catch (Exception e)
        {
            _logger.LogIncludeRulesFailed(e, context.ContextualType.Name);

            if (_throwOnError)
            {
                throw;
            }
        }
    }

    private void _HandleProperty(SchemaProcessorContext context)
    {
        if (
            context.ContextualType.Context
            is not ContextualPropertyInfo { PropertyInfo.DeclaringType: not null } contextualProperty
        )
        {
            return;
        }

        var declaringType = contextualProperty.PropertyInfo.DeclaringType;

        if (declaringType is null)
        {
            return;
        }

#pragma warning disable MA0045 // Justification: We are using a scope to resolve the validator, this is fine.
        using var scope = serviceProvider.CreateScope();
#pragma warning restore MA0045
        var declaringTypeValidator = _GetValidator(scope.ServiceProvider, declaringType);
        var propertyName = contextualProperty.PropertyInfo.Name;

        if (declaringTypeValidator is null)
        {
            return;
        }

        var propertyValidators = declaringTypeValidator.GetValidatorsByPropertyNameIgnoreCase(propertyName);

        foreach (var propertyValidator in propertyValidators)
        {
            foreach (var rule in _rules)
            {
                if (!rule.Matches(propertyValidator))
                {
                    continue;
                }

                try
                {
                    rule.Apply(new RuleContext(context, propertyName, propertyValidator));

                    _logger.LogRuleApplied(rule.RuleName, declaringType.Name, propertyName);
                }
                catch (Exception e)
                {
                    _logger.LogRuleApplyError(e, rule.RuleName, declaringType.Name, propertyName);

                    if (_throwOnError)
                    {
                        throw;
                    }
                }
            }
        }
    }

    private void _ApplyRulesToSchema(SchemaProcessorContext context, IValidator validator)
    {
        _logger.LogApplyingRules(context.ContextualType.Name);

        // Loop through properties
        foreach (var propertyName in context.Schema.Properties.Keys)
        {
            var propertyValidators = validator.GetValidatorsByPropertyNameIgnoreCase(propertyName);

            foreach (var propertyValidator in propertyValidators)
            {
                foreach (var rule in _rules)
                {
                    if (!rule.Matches(propertyValidator))
                    {
                        continue;
                    }

                    try
                    {
                        rule.Apply(new RuleContext(context, propertyName, propertyValidator));

                        _logger.LogRuleApplied(rule.RuleName, context.ContextualType.Name, propertyName);
                    }
                    catch (Exception e)
                    {
                        _logger.LogRuleApplyError(e, rule.RuleName, context.ContextualType.Name, propertyName);

                        if (_throwOnError)
                        {
                            throw;
                        }
                    }
                }
            }
        }
    }

    private void _AddRulesFromIncludedValidators(SchemaProcessorContext context, IValidator validator)
    {
        // Note: IValidatorDescriptor doesn't return IncludeRules so we need to get validators manually.
        var includeRules = (validator as IEnumerable<IValidationRule>)
            .EmptyIfNull()
            .Where(rule => rule.HasNoCondition() && rule is IIncludeRule);

        var childAdapters = includeRules
            // 2nd filter
            .SelectMany(includeRule => includeRule.Components.Select(c => c.Validator))
            .Where(x =>
                x.GetType().IsGenericType && x.GetType().GetGenericTypeDefinition() == typeof(ChildValidatorAdaptor<,>)
            )
            .ToList();

        foreach (var adapter in childAdapters)
        {
            if (adapter.GetType().GetGenericTypeDefinition() != typeof(ChildValidatorAdaptor<,>))
            {
                continue;
            }

            var adapterType = adapter.GetType();

            var adapterMethod = _MethodCache.GetValue(adapterType, _GetValidatorMethodFactory).Value;

            if (adapterMethod is null)
            {
                continue;
            }

            // Create validation context of generic type
            // Equivalent to: new ValidationContext<object>(null);
            var validationContext = Activator.CreateInstance(adapterMethod.GetParameters()[0].ParameterType, [null]);

            if (adapterMethod.Invoke(adapter, [validationContext, null]) is not IValidator includeValidator)
            {
                break;
            }

            _ApplyRulesToSchema(context, includeValidator);
            _AddRulesFromIncludedValidators(context, includeValidator);
        }
    }

    private IValidator? _GetValidator(IServiceProvider provider, Type type)
    {
        try
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(type);
            var validator = provider.GetService(validatorType) as IValidator;

            return validator;
        }
        catch (Exception e)
        {
            _logger.LogGetValidatorFailed(e, type.Name);

            return null;
        }
    }

    private static ILogger _CreateLogger(IServiceProvider provider)
    {
        var loggerFactory = provider.GetService<ILoggerFactory>();

        return loggerFactory?.CreateLogger(typeof(FluentValidationSchemaProcessor)) ?? NullLogger.Instance;
    }

    private static IReadOnlyList<FluentValidationRule> _CreateRules(IEnumerable<FluentValidationRule>? rules)
    {
        if (rules is null)
        {
            return FluentValidationRule.DefaultRules;
        }

        var map = FluentValidationRule.DefaultRules.ToDictionary(
            rule => rule.RuleName,
            rule => rule,
            StringComparer.Ordinal
        );

        foreach (var rule in rules)
        {
            map[rule.RuleName] = rule; // Add or replace rule
        }

        return map.Values.ToList();
    }
}

internal static partial class FluentValidationSchemaProcessorLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "IncludeRulesFailed",
        Level = LogLevel.Error,
        Message = "Applying IncludeRules for type '{Type}' fails"
    )]
    public static partial void LogIncludeRulesFailed(this ILogger logger, Exception exception, string type);

    [LoggerMessage(
        EventId = 2,
        EventName = "RuleApplied",
        Level = LogLevel.Debug,
        Message = "Rule '{RuleName}' applied for property '{TypeName}.{Key}'"
    )]
    public static partial void LogRuleApplied(this ILogger logger, string ruleName, string typeName, string key);

    [LoggerMessage(
        EventId = 3,
        EventName = "RuleApplyError",
        Level = LogLevel.Error,
        Message = "Error on apply rule '{RuleName}' for property '{TypeName}.{Key}'"
    )]
    public static partial void LogRuleApplyError(
        this ILogger logger,
        Exception exception,
        string ruleName,
        string typeName,
        string key
    );

    [LoggerMessage(
        EventId = 4,
        EventName = "ApplyingRules",
        Level = LogLevel.Debug,
        Message = "Applying FluentValidation rules to swagger schema for type '{Type}'"
    )]
    public static partial void LogApplyingRules(this ILogger logger, string type);

    [LoggerMessage(
        EventId = 5,
        EventName = "GetValidatorFailed",
        Level = LogLevel.Warning,
        Message = "GetValidator for type '{TypeName}' fails"
    )]
    public static partial void LogGetValidatorFailed(this ILogger logger, Exception exception, string typeName);
}
