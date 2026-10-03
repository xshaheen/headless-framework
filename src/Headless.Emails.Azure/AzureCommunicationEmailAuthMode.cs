// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Core;
using FluentValidation;

namespace Headless.Emails.Azure;

/// <summary>The single authentication mode an <see cref="AzureCommunicationEmailOptions"/> instance describes.</summary>
internal enum AzureCommunicationEmailAuthMode
{
    /// <summary>No authentication mode is configured.</summary>
    Unconfigured,

    /// <summary>More than one authentication mode is configured.</summary>
    Ambiguous,

    /// <summary>The resource connection string.</summary>
    ConnectionString,

    /// <summary>Endpoint paired with an access key.</summary>
    AccessKey,

    /// <summary>Endpoint paired with a Microsoft Entra ID token credential.</summary>
    TokenCredential,
}
