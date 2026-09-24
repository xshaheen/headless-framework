// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Security;

internal static class SecretHasherErrors
{
    public static string AlgorithmNotRegistered(string algorithm)
    {
        var remedy = string.Equals(algorithm, SecretHashAlgorithms.Argon2id, StringComparison.Ordinal)
            ? "Argon2id ships in the Headless.Security.Argon2 package: reference it and call "
                + "services.AddArgon2idSecretHashing(), or set SecretHasherOptions.Algorithm to "
                + $"'{SecretHashAlgorithms.Pbkdf2Sha256}'."
            : "Register an ISecretHashAlgorithm with that id, or change SecretHasherOptions.Algorithm.";

        return $"No secret-hashing algorithm is registered for the configured id '{algorithm}'. {remedy}";
    }
}
