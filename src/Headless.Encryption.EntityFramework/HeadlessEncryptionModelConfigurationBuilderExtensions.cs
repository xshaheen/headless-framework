// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Encryption;

namespace Microsoft.EntityFrameworkCore;

/// <summary>Maps <see cref="EncryptedValue" /> properties for a whole model.</summary>
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
}
