using Microsoft.AspNetCore.Authorization;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.SharedKernel.API.Authorization;

/// <summary>Requires the caller to hold at least the given access level.</summary>
/// <param name="Level">Minimum level.</param>
internal sealed record MinimumAccessLevelRequirement(AccessLevel Level) : IAuthorizationRequirement;
