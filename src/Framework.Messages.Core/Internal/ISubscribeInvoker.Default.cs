// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.ComponentModel;
using Framework.Messages.Messages;
using Framework.Messages.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Messages.Internal;

public class SubscribeInvoker(IServiceProvider serviceProvider, ISerializer serializer) : ISubscribeInvoker
{
    private readonly ConcurrentDictionary<string, ObjectMethodExecutor.ObjectMethodExecutor> _executors = new();

    public async Task<ConsumerExecutedResult> InvokeAsync(
        ConsumerContext context,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var methodInfo = context.ConsumerDescriptor.MethodInfo;
        var reflectedTypeHandle = methodInfo.ReflectedType!.TypeHandle.Value;
        var methodHandle = methodInfo.MethodHandle.Value;
        var key = $"{reflectedTypeHandle}_{methodHandle}";

        var executor = _executors.GetOrAdd(
            key,
            _ => ObjectMethodExecutor.ObjectMethodExecutor.Create(methodInfo, context.ConsumerDescriptor.ImplTypeInfo)
        );

        await using var scope = serviceProvider.CreateAsyncScope();

        var provider = scope.ServiceProvider;

        var obj = GetInstance(provider, context);

        var message = context.DeliverMessage;
        var parameterDescriptors = context.ConsumerDescriptor.Parameters;
        var executeParameters = new object?[parameterDescriptors.Count];
        for (var i = 0; i < parameterDescriptors.Count; i++)
        {
            var parameterDescriptor = parameterDescriptors[i];
            if (parameterDescriptor.IsFromCap)
            {
                executeParameters[i] = _GetCapProvidedParameter(parameterDescriptor, message, cancellationToken);
            }
            else
            {
                if (message.Value != null)
                {
                    // use ISerializer when reading from storage, skip other objects if not Json
                    if (serializer.IsJsonType(message.Value))
                    {
                        executeParameters[i] = serializer.Deserialize(message.Value, parameterDescriptor.ParameterType);
                    }
                    else
                    {
                        var converter = TypeDescriptor.GetConverter(parameterDescriptor.ParameterType);
                        if (converter.CanConvertFrom(message.Value.GetType()))
                        {
                            executeParameters[i] = converter.ConvertFrom(message.Value);
                        }
                        else
                        {
                            if (parameterDescriptor.ParameterType.IsInstanceOfType(message.Value))
                            {
                                executeParameters[i] = message.Value;
                            }
                            else
                            {
                                executeParameters[i] = Convert.ChangeType(
                                    message.Value,
                                    parameterDescriptor.ParameterType
                                );
                            }
                        }
                    }
                }
            }
        }

        var filter = provider.GetService<IConsumeFilter>();
        object? resultObj = null;
        try
        {
            if (filter != null)
            {
                var etContext = new ExecutingContext(context, executeParameters);
                await filter.OnSubscribeExecutingAsync(etContext).ConfigureAwait(false);
                executeParameters = etContext.Arguments;
            }

            resultObj = await _ExecuteWithParameterAsync(executor, obj, executeParameters).ConfigureAwait(false);

            if (filter != null)
            {
                var edContext = new ExecutedContext(context, resultObj);
                await filter.OnSubscribeExecutedAsync(edContext).ConfigureAwait(false);
                resultObj = edContext.Result;
            }
        }
        catch (Exception e)
        {
            if (filter != null)
            {
                var exContext = new ExceptionContext(context, e);
                await filter.OnSubscribeExceptionAsync(exContext).ConfigureAwait(false);
                if (!exContext.ExceptionHandled)
                {
                    exContext.Exception.ReThrow();
                }

                if (exContext.Result != null)
                {
                    resultObj = exContext.Result;
                }
            }
            else
            {
                throw;
            }
        }

        var callbackName = message.GetCallbackName();
        if (string.IsNullOrEmpty(callbackName))
        {
            return new ConsumerExecutedResult(resultObj, message.GetId(), null, null);
        }
        else
        {
            var capHeader = executeParameters.FirstOrDefault(x => x is MessageHeader) as MessageHeader;
            return new ConsumerExecutedResult(resultObj, message.GetId(), callbackName, capHeader?.ResponseHeader);
        }
    }

    private static object _GetCapProvidedParameter(
        ParameterDescriptor parameterDescriptor,
        Message message,
        CancellationToken cancellationToken
    )
    {
        if (typeof(CancellationToken).IsAssignableFrom(parameterDescriptor.ParameterType))
        {
            return cancellationToken;
        }

        if (parameterDescriptor.ParameterType.IsAssignableFrom(typeof(MessageHeader)))
        {
            return new MessageHeader(message.Headers);
        }

        throw new ArgumentException(parameterDescriptor.Name);
    }

    protected virtual object GetInstance(IServiceProvider provider, ConsumerContext context)
    {
        var srvType = context.ConsumerDescriptor.ServiceTypeInfo?.AsType();
        var implType = context.ConsumerDescriptor.ImplTypeInfo.AsType();

        object? obj = null;
        if (srvType != null)
        {
            obj = provider.GetServices(srvType).FirstOrDefault(o => o?.GetType() == implType);
        }

        if (obj == null)
        {
            obj = ActivatorUtilities.GetServiceOrCreateInstance(provider, implType);
        }

        return obj;
    }

    private async Task<object?> _ExecuteWithParameterAsync(
        ObjectMethodExecutor.ObjectMethodExecutor executor,
        object @class,
        object?[] parameter
    )
    {
        if (executor.IsMethodAsync)
        {
            return await executor.ExecuteAsync(@class, parameter);
        }

        return executor.Execute(@class, parameter);
    }
}
