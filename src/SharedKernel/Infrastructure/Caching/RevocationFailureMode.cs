namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>Behavior of an access-token check when the revocation blocklist is unreachable.</summary>
internal enum RevocationFailureMode
{
    /// <summary>Reject the token. Authentication fails closed; every authenticated request fails while Valkey is down.</summary>
    Deny = 0,

    /// <summary>
    /// Accept the token. Keeps authenticated traffic flowing through a Valkey outage, at the price that a token revoked
    /// by sign-out stays usable until it expires (at most the access-token lifetime). Revocation by account state
    /// (deactivation, level change) is unaffected: it is read from the database. An explicit, audited choice.
    /// </summary>
    Allow = 1,
}
