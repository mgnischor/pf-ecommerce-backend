namespace Portfolio.SharedKernel.Domain;

/// <summary>
/// The five access levels of the platform, ordered from the least to the most privileged
/// (BR-IDN-004). Access is hierarchical: a higher level includes everything a lower one may do.
/// </summary>
internal enum AccessLevel
{
    /// <summary>Anonymous visitors and registered customers: public content and their own resources.</summary>
    Public = 0,

    /// <summary>Staff who operate day to day (catalog maintenance, stock reads, order support).</summary>
    Collaborator = 1,

    /// <summary>Staff who approve business-impacting changes (prices, activation, stock adjustments, refunds).</summary>
    Manager = 2,

    /// <summary>Owns accounts and destructive actions: user management, deletions.</summary>
    Administrator = 3,

    /// <summary>Platform engineers: technical diagnostics; holds every lower level.</summary>
    Developer = 4,
}
