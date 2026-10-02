using Portfolio.IntegrationTests.Caching;
using Portfolio.IntegrationTests.Database;
using Portfolio.IntegrationTests.Messaging;

// Justification: ConfigurationFolderTests mutates a process-wide environment variable to prove that
// environment variables keep precedence over configuration/*.json. Running test classes in parallel
// would leak that value into other hosts (ai/TESTS.md §11, "shared mutable state"). Isolation of data is
// per test database (PostgresFixture), so only this environment variable keeps the assembly serial.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]
// One PostgreSQL container for the whole run (ai/TESTS.md §4.1); every test gets its own database from it.
[assembly: AssemblyFixture(typeof(PostgresFixture))]
// One Valkey for the whole run (ai/TESTS.md §4.1); every host and cache under test uses its own key prefix.
[assembly: AssemblyFixture(typeof(ValkeyFixture))]
// One RabbitMQ for the whole run (ai/TESTS.md §4.1); every publisher under test uses its own exchange names.
[assembly: AssemblyFixture(typeof(RabbitMqFixture))]
