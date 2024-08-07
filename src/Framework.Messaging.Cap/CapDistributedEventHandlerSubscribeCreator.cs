using System.Reflection;
using System.Reflection.Emit;
using DotNetCore.CAP;
using Framework.BuildingBlocks.Domains;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Messaging.Cap;

public static class CapDistributedEventHandlerSubscribeCreator
{
    private const string _AssemblyName = "DynamicCapSubscripeEventHandlerWrappers";
    private const string _NamespaceName = "EventHandlerWrappers";
    private const string _ClassName = "CapEventHandlerSubscribeDynamicType";

    public static Type Create()
    {
        return Create(_GetEventHandlerTypes());
    }

    public static Type Create(IReadOnlyCollection<Type> eventHandlerTypes)
    {
        // Base Type
        var baseType = typeof(CapEventHandlerSubscribeBase);

        var baseTypeConstructor =
            baseType.GetConstructor([typeof(IServiceProvider)])
            ?? throw new InvalidOperationException($"{nameof(CapEventHandlerSubscribeBase)} Constructor not found");

        var baseTriggerHandlerAsyncMethod =
            baseType.GetMethod(nameof(CapEventHandlerSubscribeBase.TriggerHandlerAsync))
            ?? throw new InvalidOperationException(
                $"{nameof(CapEventHandlerSubscribeBase)} {nameof(CapEventHandlerSubscribeBase.TriggerHandlerAsync)} Method not found"
            );

        // Subscribe Attribute
        var subscribeAttributeType = typeof(CapSubscribeAttribute);

        var subscribeAttributeConstructor =
            subscribeAttributeType.GetConstructor([typeof(string), typeof(bool)])
            ?? throw new InvalidOperationException($"{nameof(CapSubscribeAttribute)} Constructor not found");

        // Assembly
        var assemblyName = new AssemblyName(_AssemblyName);

        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);

        var moduleBuilder = assemblyBuilder.DefineDynamicModule(_NamespaceName);

        // Type
        var typeBuilder = moduleBuilder.DefineType(
            _ClassName,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed
        );

        // Implementation

        // 1. Extend the base class
        typeBuilder.SetParent(baseType);

        // 2. Define Constructor with IServiceProvider parameter to pass to the base class
        var constructorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(IServiceProvider)]
        );

        var constructorIlGenerator = constructorBuilder.GetILGenerator();
        constructorIlGenerator.Emit(OpCodes.Ldarg_0);
        constructorIlGenerator.Emit(OpCodes.Ldarg_1);
        constructorIlGenerator.Emit(OpCodes.Call, baseTypeConstructor);

        // 3. Add subscribe methods

        foreach (var eventHandlerType in eventHandlerTypes)
        {
            var eventType = eventHandlerType
                .GetInterfaces()
                .First(@interface =>
                    @interface.IsGenericType
                    && @interface.GetGenericTypeDefinition() == typeof(IDistributedMessageHandler<>)
                )
                .GetGenericArguments()
                .First();

            var eventName = eventType
                .GetProperty("TODO: EVENT NAME", BindingFlags.Static | BindingFlags.Public)!
                .GetValue(null);

            // 3. Define the handler method
            var handlerMethodBuilder = typeBuilder.DefineMethod(
                "HandleAsync", // TODO: support duplicated event with different names
                MethodAttributes.Public,
                typeof(ValueTask),
                [eventType]
            );

            // 3.1 Define the attribute
            var customAttributeBuilder = new CustomAttributeBuilder(subscribeAttributeConstructor, [eventName, false]);

            handlerMethodBuilder.SetCustomAttribute(customAttributeBuilder);

            // 3.2 Emit the IL for the method to execute base class TriggerHandlerAsync
            var handlerMethodIlGenerator = handlerMethodBuilder.GetILGenerator();
            handlerMethodIlGenerator.Emit(OpCodes.Ldarg_0);
            handlerMethodIlGenerator.Emit(OpCodes.Ldarg_1);
            handlerMethodIlGenerator.Emit(OpCodes.Ldtoken, eventHandlerType);
            handlerMethodIlGenerator.Emit(OpCodes.Call, baseTriggerHandlerAsyncMethod);
        }

        // 4. Create the type

        return typeBuilder.CreateType();
    }

    private static Type[] _GetEventHandlerTypes()
    {
        var types = AppDomain
            .CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type =>
                type is { IsClass: true, IsAbstract: false }
                && type.GetInterfaces()
                    .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDistributedMessageHandler<>))
            )
            .ToArray();

        return types;
    }

    [UsedImplicitly]
    public class CapEventHandlerSubscribeBase(IServiceProvider serviceProvider) : ICapSubscribe
    {
        public async ValueTask TriggerHandlerAsync<TEvent>(TEvent data, Type handler)
            where TEvent : class, IDistributedMessage
        {
            await using var scope = serviceProvider.CreateAsyncScope();

            var handlerInstance =
                (IDistributedMessageHandler<TEvent>)ActivatorUtilities.CreateInstance(serviceProvider, handler);

            await handlerInstance.HandleAsync(data);
        }
    }
}
