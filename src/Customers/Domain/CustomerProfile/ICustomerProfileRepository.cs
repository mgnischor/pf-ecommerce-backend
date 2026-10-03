using Portfolio.SharedKernel.Domain;

namespace Portfolio.Customers.Domain;

/// <summary>
/// Persistence abstraction for the <see cref="CustomerProfile"/> aggregate. The profile's identifier is the
/// account's, so <see cref="IRepository{TAggregate}.GetByIdAsync"/> is the lookup by account.
/// Defined in the domain; implemented in Infrastructure.
/// </summary>
internal interface ICustomerProfileRepository : IRepository<CustomerProfile>;
