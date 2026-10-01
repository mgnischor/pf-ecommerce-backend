namespace Portfolio.SharedKernel.API.Authorization;

/// <summary>
/// Names of the authorization policies, one per access level (BR-IDN-004). The first level, <c>Public</c>,
/// needs no policy: anonymous endpoints declare <c>[AllowAnonymous]</c> explicitly. Policies are
/// hierarchical: <see cref="Manager"/> also admits administrators and developers.
/// </summary>
internal static class AccessPolicies
{
    /// <summary>Any signed-in account, including registered customers. Ownership is checked per resource.</summary>
    public const string Authenticated = "Access.Authenticated";

    /// <summary>Staff: collaborator level or above.</summary>
    public const string Collaborator = "Access.Collaborator";

    /// <summary>Managers: manager level or above.</summary>
    public const string Manager = "Access.Manager";

    /// <summary>Administrators: administrator level or above.</summary>
    public const string Administrator = "Access.Administrator";

    /// <summary>Developers: the highest level.</summary>
    public const string Developer = "Access.Developer";
}
