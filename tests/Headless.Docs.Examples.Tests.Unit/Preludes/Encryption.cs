// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Context for the examples in docs/llms/encryption.md: the namespaces of the Headless packages the guide documents,
// the services an example calls, and placeholder application types the examples name but do not declare. An example
// that declares a type of the same name uses its own.

global using Headless.Encryption;
global using Headless.MultiTenancy;
global using Headless.Sql;
global using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace DocsPrelude
{
    public abstract partial class Ambient
    {
        protected IFieldEncryptor encryptor = null!;
        protected IBlindIndexer blindIndexer = null!;
        protected IDataKeyManager keyManager = null!;
        protected BeneficiaryDb db = null!;
        protected Beneficiary beneficiary = null!;
        protected string nationalId = "";
        protected string input = "";
    }

    public sealed class Beneficiary
    {
        public Guid Id { get; init; }

        public string TenantId { get; init; } = "";

        public EncryptedValue NationalId { get; set; }

        public EncryptedValue? Phone { get; set; }

        public byte[] NationalIdHash { get; set; } = [];

        public int NationalIdHashVersion { get; set; }
    }

    public sealed class BeneficiaryDb(DbContextOptions<BeneficiaryDb> options) : DbContext(options)
    {
        public DbSet<Beneficiary> Beneficiaries => Set<Beneficiary>();
    }

    /// <summary>Stands in for the application's own normalization of a national id.</summary>
    public static class NationalIds
    {
        public static string Normalize(string value) => value.Trim();
    }
}
