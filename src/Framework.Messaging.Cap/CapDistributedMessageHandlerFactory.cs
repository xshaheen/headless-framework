using System.Reflection;
using System.Reflection.Emit;
using DotNetCore.CAP;
using Framework.BuildingBlocks.Domains;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Messaging.Cap;

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

        var types = _GetEventHandlerTypesInAssemblies(assemblies);

        return Create(types);
    }

    public static Type Create(IReadOnlyCollection<Type> messageHandlerTypes)
    {
        // Base Type
        var baseType = typeof(CapEventHandlerSubscribeBase);

        var baseTypeConstructor =
            baseType.GetConstructor([typeof(IServiceProvider)])
            ?? throw new InvalidOperationException($"{nameof(CapEventHandlerSubscribeBase)} Constructor not found");

        var baseTriggerHandlerAsyncMethod =
            baseType.GetMethod(
                nameof(CapEventHandlerSubscribeBase.TriggerHandlerAsync),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )
            ?? throw new InvalidOperationException(
                $"{nameof(CapEventHandlerSubscribeBase)} {nameof(CapEventHandlerSubscribeBase.TriggerHandlerAsync)} Method not found"
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
        foreach (var ((eventName, eventType), list) in _GetHandlersInformation(messageHandlerTypes))
        {
            foreach (var (handlerType, methodName, groupName) in list)
            {
                // 3.1 Add the field for the handler type
                var fieldBuilder = typeBuilder.DefineField(
                    fieldName: $"_{eventType.Name}Handler",
                    type: typeof(Type),
                    attributes: FieldAttributes.Private | FieldAttributes.InitOnly
                );

                // 3.2 Assign in the constructor
                constructorIlGenerator.Emit(OpCodes.Ldarg_0); // Load this
                constructorIlGenerator.Emit(OpCodes.Ldtoken, handlerType); // Load the metadata token for the specified type
                constructorIlGenerator.Emit(OpCodes.Call, convertTypeTokenToRuntimeTypeHandleMethod); // Call the method to convert the metadata token to a runtime type handle
                constructorIlGenerator.Emit(OpCodes.Stfld, fieldBuilder); // Store the value of the field in the object

                // 3.3 Define the method (Handle a duplicated event handler name)

                var handlerMethodBuilder = typeBuilder.DefineMethod(
                    name: methodName,
                    attributes: MethodAttributes.Public | MethodAttributes.HideBySig,
                    returnType: typeof(ValueTask),
                    parameterTypes: [eventType, typeof(CapHeader), typeof(CancellationToken)]
                );

                // Add the FromCap attribute to the header parameter to inject the CapHeader
                handlerMethodBuilder
                    .DefineParameter(2, ParameterAttributes.None, "header")
                    .SetCustomAttribute(new(fromCapAttributeConstructor, []));

                // 3.4 Define the attribute & Set group property
                var customAttributeBuilder = new CustomAttributeBuilder(
                    con: subscribeAttributeConstructor,
                    constructorArgs: [eventName, false],
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
                handlerMethodIlGenerator.Emit(OpCodes.Call, baseTriggerHandlerAsyncMethod.MakeGenericMethod(eventType)); // Call the base class method
                handlerMethodIlGenerator.Emit(OpCodes.Ret); // Return from the method
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
            var eventType = handlerType
                .GetInterfaces()
                .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDistributedMessageHandler<>))
                .GetGenericArguments()[0];

            var eventName =
                eventType
                    .GetProperty(nameof(IDistributedMessage.MessageKey), BindingFlags.Static | BindingFlags.Public)!
                    .GetValue(null)
                ?? throw new InvalidOperationException(
                    $"{nameof(IDistributedMessage.MessageKey)} not found in {eventType.Name}"
                );

            var key = ((string)eventName, eventType);

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

    private static Type[] _GetEventHandlerTypesInAssemblies(IEnumerable<Assembly> assemblies)
    {
        var types = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type =>
                type is { IsClass: true, IsAbstract: false }
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
    public class CapEventHandlerSubscribeBase(IServiceProvider serviceProvider) : ICapSubscribe
    {
        public async ValueTask TriggerHandlerAsync<T>(
            T data,
            CapHeader header,
            Type handler,
            CancellationToken abortToken = default
        )
            where T : class, IDistributedMessage
        {
            await using var scope = serviceProvider.CreateAsyncScope();

            var handlerInstance =
                (IDistributedMessageHandler<T>)ActivatorUtilities.CreateInstance(serviceProvider, handler);

            await handlerInstance.HandleAsync(data, abortToken);
        }
    }

    #endregion

    #region Generated Code Example

    /*
    internal sealed class DistributedMessageHandlerCapWrapperDynamicType : CapEventHandlerSubscribeBase
    {
        private readonly Type _someMessageHandler;

        public DistributedMessageHandlerCapWrapperDynamicType(IServiceProvider serviceProvider) : base(serviceProvider)
        {
            _someMessageHandler = typeof(SomeMessageHandler);
        }

        [CapSubscribe(name: "SomeEvent", isPartial: false, Group = "SomeMessageHandler")]
        public ValueTask WireSomeMessageHandlerAsync(SomeMessage message, [FromCap] CapHeader header, CancellationToken abortToken)
        {
            return TriggerHandlerAsync(message, header, _someMessageHandler, abortToken);
        }
    }
     */

    #endregion
}
