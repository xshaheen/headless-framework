// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>Why the reflection-based Hosting helpers are unsafe to trim or compile ahead of time.</summary>
internal static class HostingTrimmingMessages
{
    public const string ConfigurationBinding =
        "Binding configuration reflects over the bound type, and trimming can remove the members it sets. In a trimmed "
        + "or native AOT app, bind the concrete type where the configuration binding source generator can see it.";

    public const string ConfigurationBindingDynamicCode =
        "Binding configuration to a generic or collection type can require code generated at runtime.";

    public const string DataAnnotations =
        "Data annotation validation reflects over the options type's properties and attributes, which trimming can "
        + "remove. Validate with FluentValidation or IValidateOptions<TOptions> in a trimmed or native AOT app.";

    public const string ConfigurationBindingWithDataAnnotations = ConfigurationBinding + " " + DataAnnotations;
}
