// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.SourceGenerator.Models;

internal sealed class JobFunctionGenerationInfo
{
    public JobFunctionGenerationInfo(
        string delegateCode,
        (string GenericTypeName, string FunctionName) requestType,
        JobFunctionDescriptorInfo descriptor
    )
    {
        DelegateCode = delegateCode;
        RequestType = requestType;
        Descriptor = descriptor;
    }

    public string DelegateCode { get; }

    public (string GenericTypeName, string FunctionName) RequestType { get; }

    public JobFunctionDescriptorInfo Descriptor { get; }
}
