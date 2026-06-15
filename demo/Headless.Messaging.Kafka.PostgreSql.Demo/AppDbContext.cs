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

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public required DbSet<Person> Persons { get; set; }
}
