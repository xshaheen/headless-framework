// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Security;

internal static class SecretHasherErrors
{
    public static string AlgorithmNotRegistered(string algorithm)
    {
        return $"The secret hasher selected the algorithm '{algorithm}', but no ISecretHashAlgorithm with that id is "
            + "registered. The Use* extension that selected it must register the algorithm in its AddServices.";
    }
}
