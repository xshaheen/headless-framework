// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.SourceGenerator.Models;

internal sealed class JobFunctionGenerationInfo(
    string delegateCode,
    (string GenericTypeName, string FunctionName) requestType,
    JobFunctionDescriptorInfo descriptor
)
{
    public string DelegateCode { get; } = delegateCode;

    public (string GenericTypeName, string FunctionName) RequestType { get; } = requestType;

    public JobFunctionDescriptorInfo Descriptor { get; } = descriptor;
}
