// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using FluentValidation;
using FluentValidation.Validators;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Translates FluentValidation rules into OpenAPI schema constraints (required, minLength, maxLength, pattern,
/// minimum, maximum, and their exclusive forms).
/// </summary>
/// <remarks>
/// <para>
/// For an object schema it resolves the type's <c>IValidator&lt;T&gt;</c> from the document-generation scope and
/// applies the rules to each property, following unconditional <c>Include()</c> chains. For a parameter bound from
/// one property of a complex type (<c>[AsParameters]</c>, <c>[FromQuery]</c>), it applies the declaring type's rules
/// for that property to the parameter schema.
/// </para>
/// <para>
/// A rule that throws is logged and skipped unless <see cref="HeadlessOpenApiOptions.ThrowOnSchemaProcessingError"/>
/// is set.
/// </para>
/// </remarks>
internal sealed class FluentValidationSchemaTransformer : IOpenApiSchemaTransformer
{
    private ILogger? _logger;

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var services = context.ApplicationServices;

        if (context.JsonTypeInfo.Kind == JsonTypeInfoKind.Object && schema.Properties is { Count: > 0 })
        {
            if (_GetValidator(services, context.JsonTypeInfo.Type) is { } validator)
            {
                var applier = new RuleApplier(services, _GetLogger(services));
                applier.ApplyToObject(schema, context.JsonTypeInfo, validator);
            }

            return Task.CompletedTask;
        }

        // The native generator also walks a parameter's item schemas with the same parameter description, so only the
        // parameter's own (root) schema takes the property's rules; RuleForEach reaches the items through RuleContext.
        if (
            context.JsonPropertyInfo is null
            && context.ParameterDescription is { } parameter
            && context.JsonTypeInfo.Type == parameter.Type
            && _GetBoundProperty(parameter) is var (declaringType, propertyName)
            && _GetValidator(services, declaringType) is { } declaringValidator
        )
        {
            var applier = new RuleApplier(services, _GetLogger(services));
            applier.ApplyToParameter(schema, declaringType, propertyName, declaringValidator);
        }

        return Task.CompletedTask;
    }

    private static (Type DeclaringType, string PropertyName)? _GetBoundProperty(ApiParameterDescription? parameter)
    {
        // MVC binds each property of a [FromQuery] complex type with property metadata.
        if (
            parameter?.ModelMetadata is
            { MetadataKind: ModelMetadataKind.Property, ContainerType: { } containerType, PropertyName: { } name }
        )
        {
            return (containerType, name);
        }

        // Minimal APIs describe each [AsParameters] property through a parameter surrogate whose member is the property.
        if (
            parameter?.ParameterDescriptor is IParameterInfoParameterDescriptor
            {
                ParameterInfo.Member: PropertyInfo { DeclaringType: { } declaringType } property,
            }
        )
        {
            return (declaringType, property.Name);
        }

        return null;
    }

    private IValidator? _GetValidator(IServiceProvider services, Type type)
    {
        return services.GetFluentValidator(type, _GetLogger(services));
    }

    // One logger per transformer, which belongs to one application's document; a race only creates a spare logger.
    private ILogger _GetLogger(IServiceProvider services)
    {
        return _logger ??=
            (ILogger?)services.GetService<ILoggerFactory>()?.CreateLogger<FluentValidationSchemaTransformer>()
            ?? NullLogger.Instance;
    }

    internal sealed class RuleApplier
    {
        private readonly ILogger _logger;
        private readonly FluentValidationRule[] _rules;
        private readonly bool _throwOnError;

        public RuleApplier(IServiceProvider services, ILogger logger)
        {
            var options =
                services.GetService<IOptions<HeadlessOpenApiOptions>>()?.Value ?? new HeadlessOpenApiOptions();
            _logger = logger;
            _rules = options.GetFluentValidationRules();
            _throwOnError = options.ThrowOnSchemaProcessingError;
        }

        public void ApplyToObject(OpenApiSchema schema, JsonTypeInfo typeInfo, IValidator validator)
        {
            _ApplyToObject(schema, typeInfo, validator);

            try
            {
                foreach (var included in validator.GetValidatorsIncludingIncludes(_logger, _throwOnError).Skip(1))
                {
                    _ApplyToObject(schema, typeInfo, included);
                }
            }
            catch (Exception e)
            {
                _logger.LogIncludeRulesFailed(e, typeInfo.Type.Name);

                if (_throwOnError)
                {
                    throw;
                }
            }
        }

        public void ApplyToParameter(
            OpenApiSchema schema,
            Type declaringType,
            string propertyName,
            IValidator validator
        )
        {
            foreach (
                var (propertyValidator, isCollectionRule) in validator.GetValidatorsByPropertyNameIgnoreCase(
                    propertyName
                )
            )
            {
                _Apply(new RuleContext(schema, propertyName, propertyValidator, isCollectionRule), declaringType.Name);
            }
        }

        /// <summary>
        /// Applies <paramref name="validators"/> to the property <paramref name="propertyKey"/> of
        /// <paramref name="schema"/>, which lists it the way an object schema lists a member.
        /// </summary>
        public void ApplyToProperty(
            OpenApiSchema schema,
            string propertyKey,
            IEnumerable<(IPropertyValidator Validator, bool IsCollectionRule)> validators,
            string typeName
        )
        {
            foreach (var (propertyValidator, isCollectionRule) in validators)
            {
                _Apply(new RuleContext(schema, propertyKey, propertyValidator, isCollectionRule), typeName);
            }
        }

        private void _ApplyToObject(OpenApiSchema schema, JsonTypeInfo typeInfo, IValidator validator)
        {
            _logger.LogApplyingRules(typeInfo.Type.Name);

            // Snapshot the keys: a rule may replace a property's schema while it collapses a nullable reference.
            foreach (var propertyKey in schema.Properties!.Keys.ToArray())
            {
                var memberName = _GetMemberName(typeInfo, propertyKey);

                foreach (
                    var (propertyValidator, isCollectionRule) in validator.GetValidatorsByPropertyNameIgnoreCase(
                        memberName
                    )
                )
                {
                    _Apply(
                        new RuleContext(schema, propertyKey, propertyValidator, isCollectionRule),
                        typeInfo.Type.Name
                    );
                }
            }
        }

        private void _Apply(RuleContext context, string typeName)
        {
            foreach (var rule in _rules)
            {
                if (!rule.Matches(context.PropertyValidator))
                {
                    continue;
                }

                try
                {
                    rule.Apply(context);
                    _logger.LogRuleApplied(rule.RuleName, typeName, context.PropertyKey);
                }
                catch (Exception e)
                {
                    _logger.LogRuleApplyError(e, rule.RuleName, typeName, context.PropertyKey);

                    if (_throwOnError)
                    {
                        throw;
                    }
                }
            }
        }

        private static string _GetMemberName(JsonTypeInfo typeInfo, string propertyKey)
        {
            foreach (var property in typeInfo.Properties)
            {
                if (string.Equals(property.Name, propertyKey, StringComparison.Ordinal))
                {
                    return property.AttributeProvider is MemberInfo member ? member.Name : propertyKey;
                }
            }

            return propertyKey;
        }
    }
}

internal static partial class FluentValidationSchemaTransformerLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "IncludeRulesFailed",
        Level = LogLevel.Error,
        Message = "Applying IncludeRules for type '{Type}' fails"
    )]
    internal static partial void LogIncludeRulesFailed(this ILogger logger, Exception exception, string type);

    [LoggerMessage(
        EventId = 2,
        EventName = "RuleApplied",
        Level = LogLevel.Debug,
        Message = "Rule '{RuleName}' applied for property '{TypeName}.{Key}'"
    )]
    internal static partial void LogRuleApplied(this ILogger logger, string ruleName, string typeName, string key);

    [LoggerMessage(
        EventId = 3,
        EventName = "RuleApplyError",
        Level = LogLevel.Error,
        Message = "Error on apply rule '{RuleName}' for property '{TypeName}.{Key}'"
    )]
    internal static partial void LogRuleApplyError(
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
        Message = "Applying FluentValidation rules to the OpenAPI schema of type '{Type}'"
    )]
    internal static partial void LogApplyingRules(this ILogger logger, string type);

    [LoggerMessage(
        EventId = 5,
        EventName = "GetValidatorFailed",
        Level = LogLevel.Warning,
        Message = "GetValidator for type '{TypeName}' fails"
    )]
    internal static partial void LogGetValidatorFailed(this ILogger logger, Exception exception, string typeName);

    [LoggerMessage(
        EventId = 6,
        EventName = "GetChildValidatorFailed",
        Level = LogLevel.Warning,
        Message = "GetChildValidator for validator '{TypeName}' fails; skipping the child validator"
    )]
    internal static partial void LogGetChildValidatorFailed(this ILogger logger, Exception exception, string typeName);
}
