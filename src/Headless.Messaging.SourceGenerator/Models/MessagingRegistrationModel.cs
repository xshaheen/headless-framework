// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Messaging.SourceGenerator.Models;

/// <summary>The complete input of the emitter for one assembly. Contains no locations or diagnostics.</summary>
internal sealed record MessagingRegistrationModel(
    string AssemblyName,
    EquatableArray<ConsumerRegistrationModel> Consumers
);
