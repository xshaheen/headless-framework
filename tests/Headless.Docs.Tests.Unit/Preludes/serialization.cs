// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application options provider, models, and variables the serialization guide's examples assume.

global using static SerializationAmbient;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Headless.Serializer;

public sealed class MyJsonOptionsProvider : IJsonOptionsProvider
{
    public JsonSerializerOptions GetSerializeOptions() => JsonConstants.CreateWebJsonOptions();

    public JsonSerializerOptions GetDeserializeOptions() => JsonConstants.CreateWebJsonOptions();
}

public sealed class MyModel
{
    public string? InternalField { get; set; }
}

// Shaped like a source-generated JsonSerializerContext, whose generator this check does not run.
public static class OrderJsonContext
{
    public static IJsonTypeInfoResolver Default => null!;
}

#pragma warning disable IDE1006 // Ambient members mirror the locals the examples use.
public static class SerializationAmbient
{
    public static byte[] requestBodyUtf8 => [];
}
