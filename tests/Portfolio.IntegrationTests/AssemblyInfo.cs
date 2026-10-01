using Portfolio.IntegrationTests.Database;

// Justification: ConfigurationFolderTests mutates a process-wide environment variable to prove that
// environment variables keep precedence over configuration/*.json. Running test classes in parallel
// would leak that value into other hosts (ai/TESTS.md §11, "shared mutable state"). Isolation of data is
// per test database (PostgresFixture), so only this environment variable keeps the assembly serial.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]
// One PostgreSQL container for the whole run (ai/TESTS.md §4.1); every test gets its own database from it.
[assembly: AssemblyFixture(typeof(PostgresFixture))]
