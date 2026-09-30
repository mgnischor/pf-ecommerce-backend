// Justification: ConfigurationFolderTests mutates a process-wide environment variable to prove that
// environment variables keep precedence over configuration/*.json. Running test classes in parallel
// would leak that value into other hosts (ai/TESTS.md §11, "shared mutable state"). The suite is small;
// revisit when the Testcontainers fixtures arrive.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]
