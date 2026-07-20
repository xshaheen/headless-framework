// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System.Reflection.Emit;

/// <summary>Convenience extensions for emitting common IL sequences with an <see cref="ILGenerator"/>.</summary>
[PublicAPI]
public static class HeadlessIlGeneratorExtensions
{
    /// <summary>Emits a <see cref="OpCodes.Ret"/> instruction that returns from the current method.</summary>
    /// <param name="generator">The IL generator to emit into.</param>
    public static void Return(this ILGenerator generator)
    {
        generator.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Loads argument 0 (the instance) and coerces it to <paramref name="type"/>, unboxing value types and casting
    /// reference types.
    /// </summary>
    /// <param name="generator">The IL generator to emit into.</param>
    /// <param name="type">The declared type of the instance being loaded.</param>
    public static void PushInstance(this ILGenerator generator, Type type)
    {
        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(type.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, type);
    }

    /// <summary>Emits the instruction to box the value on the stack when <paramref name="type"/> is a value type, otherwise casts it.</summary>
    /// <param name="generator">The IL generator to emit into.</param>
    /// <param name="type">The type of the value currently on the evaluation stack.</param>
    public static void BoxIfNeeded(this ILGenerator generator, Type type)
    {
        generator.Emit(type.IsValueType ? OpCodes.Box : OpCodes.Castclass, type);
    }

    /// <summary>Emits the instruction to unbox the value on the stack when <paramref name="type"/> is a value type, otherwise casts it.</summary>
    /// <param name="generator">The IL generator to emit into.</param>
    /// <param name="type">The target type of the value currently on the evaluation stack.</param>
    public static void UnboxIfNeeded(this ILGenerator generator, Type type)
    {
        generator.Emit(type.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, type);
    }

    /// <summary>
    /// Emits a call to <paramref name="methodInfo"/>, using <see cref="OpCodes.Call"/> for sealed or non-virtual
    /// methods and <see cref="OpCodes.Callvirt"/> for virtual ones.
    /// </summary>
    /// <param name="generator">The IL generator to emit into.</param>
    /// <param name="methodInfo">The method to call.</param>
    public static void CallMethod(this ILGenerator generator, MethodInfo methodInfo)
    {
        generator.Emit(methodInfo.IsFinal || !methodInfo.IsVirtual ? OpCodes.Call : OpCodes.Callvirt, methodInfo);
    }
}
