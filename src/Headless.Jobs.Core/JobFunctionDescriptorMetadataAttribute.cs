// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;
using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Models;

namespace Headless.Jobs;

/// <summary>Generated assembly metadata that exposes a durable job-function identity to consuming generators.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class JobFunctionDescriptorMetadataAttribute(
    string functionName,
    string contractVersion = JobContract.InitialVersion
) : Attribute
{
    /// <summary>Payload schema version of the generated durable contract.</summary>
    public string ContractVersion { get; } = JobContract.ValidateVersion(contractVersion);

    /// <summary>The generated durable function name.</summary>
    public string FunctionName { get; } = functionName ?? throw new ArgumentNullException(nameof(functionName));
}
