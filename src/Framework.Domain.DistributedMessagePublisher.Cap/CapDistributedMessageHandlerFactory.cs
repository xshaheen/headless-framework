// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Reflection.Emit;
using DotNetCore.CAP;
using Framework.Domain.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Domain;

using MessageHandlerMap = Dictionary<
    (string MessageName, Type MessageType),
    List<(Type HandlerType, string MethodName, string GroupName)>
>;

public static class CapDistributedMessageHandlerFactory
{
    private const string _AssemblyName = "DistributedMessageHandlerCapWrappers";
    private const string _ClassName = "DistributedMessageHandlerCapWrapperDynamicType";

    public static Type Create()
    {
        var assemblies = AppDomain
            .CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.FullName is not null && !_IsSystemAssembly(assembly.FullName));

        var types = _GetMessageHandlerTypesInAssemblies(assemblies);

        return Create(types);
    }

    public static Type Create(IReadOnlyCollection<TypeInfo> messageHandlerTypes)
    {
        // Base Type
        var baseType = typeof(CapMessageHandlerSubscribeBase);

        var baseTypeConstructor =
            baseType.GetConstructor([typeof(IServiceProvider)])
            ?? throw new InvalidOperationException($"{nameof(CapMessageHandlerSubscribeBase)} Constructor not found");

        var baseTriggerHandlerAsyncMethod =
            baseType.GetMethod(
                nameof(CapMessageHandlerSubscribeBase.TriggerHandlerAsync),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )
            ?? throw new InvalidOperationException(
                $"{nameof(CapMessageHandlerSubscribeBase)} {nameof(CapMessageHandlerSubscribeBase.TriggerHandlerAsync)} Method not found"
            );

        // Subscribe Attribute
        var subscribeAttributeType = typeof(CapSubscribeAttribute);

        var subscribeAttributeConstructor =
            subscribeAttributeType.GetConstructor([typeof(string), typeof(bool)])
            ?? throw new InvalidOperationException($"{nameof(CapSubscribeAttribute)} Constructor not found");

        var subscribeAttributeGroupProperty =
            subscribeAttributeType.GetProperty(
                nameof(CapSubscribeAttribute.Group),
                BindingFlags.Public | BindingFlags.Instance
            )
            ?? throw new InvalidOperationException(
                $"{nameof(CapSubscribeAttribute)} {nameof(CapSubscribeAttribute.Group)} Property not found"
            );

        // FromCap Attribute
        var fromCapAttributeType = typeof(FromCapAttribute);

        var fromCapAttributeConstructor =
            fromCapAttributeType.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                Type.EmptyTypes,
                modifiers: null
            ) ?? throw new InvalidOperationException($"{nameof(FromCapAttribute)} Constructor not found");

        // Type
        var convertTypeTokenToRuntimeTypeHandleMethod =
            typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle), [typeof(RuntimeTypeHandle)])
            ?? throw new InvalidOperationException($"{nameof(Type)}.{nameof(Type.GetTypeFromHandle)} Method not found");

        // Assembly
        var assemblyName = new AssemblyName(_AssemblyName);

        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);

        var moduleBuilder = assemblyBuilder.DefineDynamicModule(_AssemblyName);

        // Type
        var typeBuilder = moduleBuilder.DefineType(
            name: _ClassName,
            attr: TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed
        );

        // Implementation

        // 1. Extend the base class
        typeBuilder.SetParent(baseType);

        // 2. Define Constructor & Fields
        var constructorBuilder = typeBuilder.DefineConstructor(
            attributes: MethodAttributes.Public,
            callingConvention: CallingConventions.Standard,
            parameterTypes: [typeof(IServiceProvider)]
        );

        var constructorIlGenerator = constructorBuilder.GetILGenerator();
        constructorIlGenerator.Emit(OpCodes.Ldarg_0); // Load this
        constructorIlGenerator.Emit(OpCodes.Ldarg_1); // Load serviceProvider
        constructorIlGenerator.Emit(OpCodes.Call, baseTypeConstructor); // Call the base constructor

        // 3. Add subscribe methods
        foreach (var ((messageName, messageType), list) in _GetHandlersInformation(messageHandlerTypes))
        {
            foreach (var (handlerType, methodName, groupName) in list)
            {
                // 3.1 Add the field for the handler type
                var fieldBuilder = typeBuilder.DefineField(
                    fieldName: $"_{messageType.Name}Handler",
                    type: typeof(Type),
                    attributes: FieldAttributes.Private | FieldAttributes.InitOnly
                );

                // 3.2 Assign in the constructor
                constructorIlGenerator.Emit(OpCodes.Ldarg_0); // Load this
                constructorIlGenerator.Emit(OpCodes.Ldtoken, handlerType); // Load the metadata token for the specified type
                constructorIlGenerator.Emit(OpCodes.Call, convertTypeTokenToRuntimeTypeHandleMethod); // Call the method to convert the metadata token to a runtime type handle
                constructorIlGenerator.Emit(OpCodes.Stfld, fieldBuilder); // Store the value of the field in the object

                // 3.3 Define the method (Handle a duplicated message handler name)

                var handlerMethodBuilder = typeBuilder.DefineMethod(
                    name: methodName,
                    attributes: MethodAttributes.Public | MethodAttributes.HideBySig,
                    returnType: typeof(ValueTask),
                    parameterTypes: [messageType, typeof(CapHeader), typeof(CancellationToken)]
                );

                // Add the FromCap attribute to the header parameter to inject the CapHeader
                handlerMethodBuilder
                    .DefineParameter(2, ParameterAttributes.None, "header")
                    .SetCustomAttribute(new(fromCapAttributeConstructor, []));

                // 3.4 Define the attribute & Set group property
                var customAttributeBuilder = new CustomAttributeBuilder(
                    con: subscribeAttributeConstructor,
                    constructorArgs: [messageName, false],
                    namedProperties: [subscribeAttributeGroupProperty],
                    propertyValues: [groupName]
                );

                handlerMethodBuilder.SetCustomAttribute(customAttributeBuilder);

                // 3.5 Emit the IL for the method to execute base class TriggerHandlerAsync
                var handlerMethodIlGenerator = handlerMethodBuilder.GetILGenerator();

                handlerMethodIlGenerator.Emit(OpCodes.Ldarg_0); // Load argument 0 (this) to the stack
                handlerMethodIlGenerator.Emit(OpCodes.Ldarg_1); // Load argument 1 (message) to the stack
                handlerMethodIlGenerator.Emit(OpCodes.Ldarg_2); // Load argument 2 (header) to the stack
                handlerMethodIlGenerator.Emit(OpCodes.Ldarg_0); // Load argument 0 (this) to the stack
                handlerMethodIlGenerator.Emit(OpCodes.Ldfld, fieldBuilder); // Load the field value to the stack
                handlerMethodIlGenerator.Emit(OpCodes.Ldarg_3); // Load argument 3 (abortToken) to the stack
                handlerMethodIlGenerator.CallMethod(baseTriggerHandlerAsyncMethod.MakeGenericMethod(messageType)); // Call the base class method
                handlerMethodIlGenerator.Return();
            }
        }

        constructorIlGenerator.Emit(OpCodes.Ret); // Return from the constructor

        // 4. Create the type
        var type = typeBuilder.CreateType();

        return type;
    }

    private static MessageHandlerMap _GetHandlersInformation(IReadOnlyCollection<Type> handlerTypes)
    {
        var map = new MessageHandlerMap();

        foreach (var handlerType in handlerTypes)
        {
            var messageType = handlerType
                .GetInterfaces()
                .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDistributedMessageHandler<>))
                .GetGenericArguments()[0];

            var messageName = MessageName.GetFrom(messageType);
            var key = (messageName, messageType);

            if (!map.TryGetValue(key, out var handlers))
            {
                var methodName = $"Wire{handlerType.Name}Async";
                var groupName = handlerType.Name;

                map.Add(key, [(handlerType, methodName, groupName)]);
            }
            else
            {
                var methodName = $"Wire{handlerType.Name}{handlers.Count}Async";
                var groupName = $"{handlerType.Name}{handlers.Count}";

                handlers.Add((handlerType, methodName, groupName));
            }
        }

        return map;
    }

    #region Get Handlers In Assemblies

    private static TypeInfo[] _GetMessageHandlerTypesInAssemblies(IEnumerable<Assembly> assemblies)
    {
        var types = assemblies
            .SelectMany(assembly => assembly.GetConstructibleDefinedTypes())
            .Where(type =>
                type is { IsClass: true }
                && type.GetInterfaces()
                    .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDistributedMessageHandler<>))
            )
            .ToArray();

        return types;
    }

    private static bool _IsSystemAssembly(string assemblyFullName)
    {
        return assemblyFullName.StartsWith("System.", StringComparison.Ordinal)
            || assemblyFullName.StartsWith("Microsoft.", StringComparison.Ordinal);
    }

    #endregion

    #region Base Class

    [UsedImplicitly]
    public class CapMessageHandlerSubscribeBase(IServiceProvider serviceProvider) : ICapSubscribe
    {
        public async ValueTask TriggerHandlerAsync<T>(
            T data,
            CapHeader header,
            Type handler,
            CancellationToken cancellationToken = default
        )
            where T : class, IDistributedMessage
        {
            await using var scope = serviceProvider.CreateAsyncScope();

            var handlerInstance =
                (IDistributedMessageHandler<T>)ActivatorUtilities.CreateInstance(serviceProvider, handler);

            foreach (var h in header)
            {
                if (h.Value is not null)
                {
                    data.Headers[h.Key] = h.Value;
                }
            }

            await handlerInstance.HandleAsync(data, cancellationToken);
        }
    }

    #endregion

    #region Generated Code Example

    /*
    internal sealed class DistributedMessageHandlerCapWrapperDynamicType : CapMessageHandlerSubscribeBase
    {
        private readonly Type _someMessageHandler;

        public DistributedMessageHandlerCapWrapperDynamicType(IServiceProvider serviceProvider) : base(serviceProvider)
        {
            _someMessageHandler = typeof(SomeMessageHandler);
        }

        [CapSubscribe(name: "SomeMessage", isPartial: false, Group = "SomeMessageHandler")]
        public ValueTask WireSomeMessageHandlerAsync(SomeMessage message, [FromCap] CapHeader header, CancellationToken cancellationToken)

        {
            return TriggerHandlerAsync(message, header, _someMessageHandler, cancellationToken);
        }
    }
     */

    #endregion
}
