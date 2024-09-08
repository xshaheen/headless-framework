#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Reflection.Emit;

[PublicAPI]
public static class IlGeneratorExtensions
{
    public static void PushInstance(this ILGenerator generator, Type type)
    {
        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(type.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, type);
    }

    public static void BoxIfNeeded(this ILGenerator generator, Type type)
    {
        generator.Emit(type.IsValueType ? OpCodes.Box : OpCodes.Castclass, type);
    }

    public static void UnboxIfNeeded(this ILGenerator generator, Type type)
    {
        generator.Emit(type.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, type);
    }

    public static void CallMethod(this ILGenerator generator, MethodInfo methodInfo)
    {
        generator.Emit(methodInfo.IsFinal || !methodInfo.IsVirtual ? OpCodes.Call : OpCodes.Callvirt, methodInfo);
    }

    public static void Return(this ILGenerator generator)
    {
        generator.Emit(OpCodes.Ret);
    }
}
