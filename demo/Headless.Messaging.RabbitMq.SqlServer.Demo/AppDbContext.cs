using Microsoft.EntityFrameworkCore;

namespace Demo;

public class Person
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public int Age { get; set; }

    public override string ToString()
    {
        return $"Name:{Name}, Age:{Age}";
    }
}

// Configured in Program.cs via AddDbContext so the commit-coordination EF interceptor (a DI service) can be wired
// into the options — EF Core does not auto-discover IInterceptor registrations from the application container.
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public const string ConnectionString =
        "Server=127.0.0.1;Database=tempdb;User Id=sa;Password=yourStrong(!)Password;TrustServerCertificate=True";

    public required DbSet<Person> Persons { get; set; }
}
