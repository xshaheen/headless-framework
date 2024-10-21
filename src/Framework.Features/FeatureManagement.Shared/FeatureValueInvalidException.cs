// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Volo.Abp.FeatureManagement;

[Serializable]
public class FeatureValueInvalidException : BusinessException
{
    public FeatureValueInvalidException(string name)
        : base(FeatureManagementDomainErrorCodes.FeatureValueInvalid)
    {
        WithData("0", name);
    }
}
