// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Imaging;

/// <summary>
/// A builder returned by <c>AddImaging</c> that carries the <see cref="IServiceCollection"/> through
/// the imaging setup pipeline. Provider packages extend this builder with <c>Use*</c> extension members.
/// </summary>
/// <param name="Services">The service collection being configured.</param>
public readonly record struct AddImagingBuilder(IServiceCollection Services);
