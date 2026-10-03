namespace Portfolio.Customers.Application;

/// <summary>Request for the profile of the authenticated customer.</summary>
/// <param name="AccountId">The caller's account, taken from the validated token; never from the request (BR-CUS-007).</param>
internal sealed record GetCustomerProfileQuery(Guid AccountId);
