using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Identity.API.Contracts;
using Portfolio.Identity.Application;
using Portfolio.SharedKernel.API;

namespace Portfolio.Identity.API.Controllers;

/// <summary>Publishes the issuer's public keys so other services can verify tokens (RFC 7517).</summary>
[Route(".well-known/jwks.json")]
internal sealed class JwksController(IPublicKeySource keys) : ApiControllerBase
{
    /// <summary>
    /// Returns the public half of every key in rotation, the active one and the retired ones still needed to
    /// verify unexpired tokens. Private key parameters are never included. Cacheable for five minutes.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    [ProducesResponseType<JsonWebKeySetResponse>(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        var response = new JsonWebKeySetResponse([
            .. keys.GetPublicKeys()
                .Select(key => new JsonWebKeyResponse(
                    key.KeyType,
                    key.Curve,
                    key.X,
                    key.Y,
                    key.KeyId,
                    key.Use,
                    key.Algorithm
                )),
        ]);

        return Ok(response);
    }
}
