using System.Security.Cryptography;
using System.Text;

namespace Framework.BuildingBlocks.Abstractions;

public interface IHashService
{
    string Create(string value, string salt);
}

public sealed class HashService(int iterations, int size) : IHashService
{
    public int Iterations { get; } = iterations;

    public int Size { get; } = size;

    public string Create(string value, string salt)
    {
        using var algorithm = new Rfc2898DeriveBytes(
            value,
            Encoding.UTF8.GetBytes(salt),
            Iterations,
            HashAlgorithmName.SHA512
        );

        return Convert.ToBase64String(algorithm.GetBytes(Size));
    }
}
