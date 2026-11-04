// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Encryption;

namespace Microsoft.EntityFrameworkCore;

/// <summary>Maps <see cref="EncryptedValue" /> and <see cref="BlindIndex" /> properties for a whole model.</summary>
[PublicAPI]
public static class HeadlessEncryptionModelConfigurationBuilderExtensions
{
    /// <summary>
    /// Stores every <see cref="EncryptedValue" /> and <see cref="Nullable{T}" /> <see cref="EncryptedValue" /> property
    /// as a binary column holding the frame, through <see cref="EncryptedValueConverter" />. Call it from
    /// <c>ConfigureConventions</c>.
    /// </summary>
    /// <param name="configurationBuilder">The model configuration builder.</param>
    /// <returns>The builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configurationBuilder" /> is <see langword="null" />.</exception>
    public static ModelConfigurationBuilder AddEncryptedValueConversions(
        this ModelConfigurationBuilder configurationBuilder
    )
    {
        Argument.IsNotNull(configurationBuilder);

        configurationBuilder.Properties<EncryptedValue>().HaveConversion<EncryptedValueConverter>();
        configurationBuilder.Properties<EncryptedValue?>().HaveConversion<EncryptedValueConverter>();

        return configurationBuilder;
    }

    /// <summary>
    /// Stores every <see cref="BlindIndex" /> and <see cref="Nullable{T}" /> <see cref="BlindIndex" /> property as a
    /// text column holding <see cref="BlindIndex.ToBase64Url" />, through <see cref="BlindIndexConverter" />. Call it
    /// from <c>ConfigureConventions</c> when the model's lookup columns are text; map a binary hash column per property
    /// instead.
    /// </summary>
    /// <param name="configurationBuilder">The model configuration builder.</param>
    /// <returns>The builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configurationBuilder" /> is <see langword="null" />.</exception>
    public static ModelConfigurationBuilder AddBlindIndexConversions(
        this ModelConfigurationBuilder configurationBuilder
    )
    {
        Argument.IsNotNull(configurationBuilder);

        configurationBuilder.Properties<BlindIndex>().HaveConversion<BlindIndexConverter>();
        configurationBuilder.Properties<BlindIndex?>().HaveConversion<BlindIndexConverter>();

        return configurationBuilder;
    }
}
