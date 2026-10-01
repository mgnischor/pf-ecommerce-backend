using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.UnitTests.SharedKernel.Infrastructure;

public sealed class DesignTimeOptionsTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"pf-conn-{Guid.NewGuid():N}.txt");

    public void Dispose()
    {
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }
    }

    private static Func<string, string?> Environment(params (string Name, string Value)[] variables) =>
        name => variables.Where(variable => variable.Name == name).Select(variable => variable.Value).FirstOrDefault();

    [Fact]
    public void Should_fall_back_to_a_placeholder_that_carries_no_credentials_when_nothing_is_set()
    {
        var connection = DesignTimeOptions.ResolveConnection(Environment());

        connection.ShouldContain("Host=localhost");
        connection.ShouldNotContain("Password", Case.Insensitive);
    }

    [Fact]
    public void Should_use_the_connection_variable_when_set()
    {
        DesignTimeOptions
            .ResolveConnection(Environment((DesignTimeOptions.ConnectionVariable, "Host=db;Database=x")))
            .ShouldBe("Host=db;Database=x");
    }

    [Fact]
    public void Should_read_the_connection_from_a_secret_file_and_prefer_it_over_the_variable()
    {
        File.WriteAllText(_file, "Host=db;Database=x;Username=app_migrator;Password=from-file\n");

        var connection = DesignTimeOptions.ResolveConnection(
            Environment(
                (DesignTimeOptions.ConnectionFileVariable, _file),
                (DesignTimeOptions.ConnectionVariable, "Host=other")
            )
        );

        connection.ShouldBe("Host=db;Database=x;Username=app_migrator;Password=from-file");
    }

    [Fact]
    public void Should_fail_loudly_when_the_named_secret_file_is_missing_instead_of_connecting_somewhere_else()
    {
        Should.Throw<FileNotFoundException>(() =>
            DesignTimeOptions.ResolveConnection(Environment((DesignTimeOptions.ConnectionFileVariable, _file)))
        );
    }
}
