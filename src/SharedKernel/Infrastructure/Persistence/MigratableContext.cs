namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>A registered context that the migrator and the tests must bring up to date.</summary>
/// <param name="ContextType">The <see cref="ModuleDbContext"/> type.</param>
internal sealed record MigratableContext(Type ContextType);
