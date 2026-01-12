// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Validators;
using Framework.Api.SchemaProcessors.FluentValidation;
using Framework.Api.SchemaProcessors.FluentValidation.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Namotion.Reflection;
using NJsonSchema.Generation;

namespace Framework.Api.SchemaProcessors;

/// <summary>
/// Swagger <see cref="ISchemaProcessor"/> that uses FluentValidation validators instead System.ComponentModel based attributes.
/// </summary>
public sealed class FluentValidationSchemaProcessor(
    IServiceProvider serviceProvider,
    IEnumerable<FluentValidationRule>? rules = null
) : ISchemaProcessor
{
    private static readonly ConcurrentDictionary<Type, MethodInfo?> _MethodCache = new();

    private readonly ILogger _logger = _CreateLogger(serviceProvider);
    private readonly IReadOnlyList<FluentValidationRule> _rules = _CreateRules(rules);

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
            _logger.LogWarning(0, e, "Applying IncludeRules for type '{Type}' fails", context.ContextualType.Name);
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

                    _logger.LogDebug(
                        "Rule '{RuleName}' applied for property '{TypeName}.{Key}'",
                        rule.RuleName,
                        declaringType.Name,
                        propertyName
                    );
                }
                catch (Exception e)
                {
                    _logger.LogWarning(
                        0,
                        e,
                        "Error on apply rule '{RuleName}' for property '{TypeName}.{Key}'",
                        rule.RuleName,
                        propertyName,
                        propertyName
                    );
                }
            }
        }
    }

    private void _ApplyRulesToSchema(SchemaProcessorContext context, IValidator validator)
    {
        _logger.LogDebug(
            "Applying FluentValidation rules to swagger schema for type '{Type}'",
            context.ContextualType.Name
        );

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

                        _logger.LogDebug(
                            "Rule '{RuleName}' applied for property '{TypeName}.{Key}'",
                            rule.RuleName,
                            context.ContextualType.Name,
                            propertyName
                        );
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(
                            e,
                            "Error on apply rule '{RuleName}' for property '{TypeName}.{Key}'",
                            rule.RuleName,
                            context.ContextualType.Name,
                            propertyName
                        );
                    }
                }
            }
        }
    }

    private void _AddRulesFromIncludedValidators(SchemaProcessorContext context, IValidator validator)
    {
        // Note: IValidatorDescriptor doesn't return IncludeRules so we need to get validators manually.
        var includeRules = ValidationExtensions
            .EmptyIfNull(validator as IEnumerable<IValidationRule>)
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

#pragma warning disable REFL017, REFL003 // Justification: Already of type ChildValidatorAdaptor<,>
            var adapterMethod = _MethodCache.GetOrAdd(
                adapterType,
                t => t.GetMethod(nameof(ChildValidatorAdaptor<,>.GetValidator))
            );
#pragma warning restore REFL017, REFL003

            if (adapterMethod is null)
            {
                continue;
            }

            // Create validation context of generic type
            // Equivalent to: new ValidationContext<object>(null);
            var validationContext = Activator.CreateInstance(adapterMethod.GetParameters()[0].ParameterType, [null!]);

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
            _logger.LogWarning(0, e, "GetValidator for type '{TypeName}' fails", type.Name);

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
