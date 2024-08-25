using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Validators;
using Framework.Api.Swagger.Nswag.SchemaProcessors.FluentValidation;
using Framework.Api.Swagger.Nswag.SchemaProcessors.FluentValidation.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Namotion.Reflection;
using NJsonSchema.Generation;

namespace Framework.Api.Swagger.Nswag.SchemaProcessors;

/// <summary>
/// Swagger <see cref="ISchemaProcessor"/> that uses FluentValidation validators instead System.ComponentModel based attributes.
/// </summary>
public sealed class FluentValidationSchemaProcessor : ISchemaProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly IReadOnlyList<FluentValidationRule> _rules;

    public FluentValidationSchemaProcessor(
        IServiceProvider serviceProvider,
        IEnumerable<FluentValidationRule>? rules = null
    )
    {
        _serviceProvider = serviceProvider;
        _logger =
            serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(FluentValidationSchemaProcessor))
            ?? NullLogger.Instance;
        _rules = FluentValidationRule.DefaultRules;

        if (rules is null)
        {
            return;
        }

        var ruleMap = _rules.ToDictionary(rule => rule.RuleName, rule => rule, StringComparer.Ordinal);

        foreach (var rule in rules)
        {
            // Add or replace rule
            ruleMap[rule.RuleName] = rule;
        }

        _rules = ruleMap.Values.ToList();
    }

    public void Process(SchemaProcessorContext context)
    {
        if (context.Schema is { IsObject: true, Properties.Count: > 0 })
        {
            var validator = _GetValidator(context.ContextualType);

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

            return;
        }

        // Not an object but is a property of an object type
        // (e.g. a class that used as a query parameters so it's not an object in swagger)
        // but we still have a validator for its declaring type so we can apply rules to it.
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

        var declaringTypeValidator = _GetValidator(declaringType);
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

    private IValidator? _GetValidator(Type type)
    {
        try
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(type);
            var validator = _serviceProvider.GetService(validatorType) as IValidator;

            return validator;
        }
        catch (Exception e)
        {
            _logger.LogWarning(0, e, "GetValidator for type '{TypeName}' fails", type.Name);

            return null;
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
            var adapterMethod = adapterType.GetMethod(nameof(ChildValidatorAdaptor<object, object>.GetValidator));
#pragma warning restore REFL017, REFL003

            if (adapterMethod is null)
            {
                continue;
            }

            // Create validation context of generic type
            // Equivalent to: new ValidationContext<object>(null);
            var validationContext = Activator.CreateInstance(
                adapterMethod.GetParameters()[0].ParameterType,
                new object[] { null! }
            );

            if (adapterMethod.Invoke(adapter, [validationContext, null]) is not IValidator includeValidator)
            {
                break;
            }

            _ApplyRulesToSchema(context, includeValidator);
            _AddRulesFromIncludedValidators(context, includeValidator);
        }
    }
}
