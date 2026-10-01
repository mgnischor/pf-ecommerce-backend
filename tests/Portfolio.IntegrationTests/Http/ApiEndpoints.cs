namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// The mapped API surface as a contract snapshot: adding, removing, re-routing, or changing the access level
/// of an endpoint must be a deliberate change to this table (and to <c>Portfolio.http</c>).
/// </summary>
internal static class ApiEndpoints
{
    /// <summary>Marker of an endpoint that needs no credentials.</summary>
    public const string Anonymous = "anonymous";

    private const string Id = "0199f3a2-7c10-7d3e-8a51-2b9d4c6e1f01";

    /// <summary>
    /// Method, route template as mapped, a concrete URL, and the minimum access level: <c>anonymous</c> for
    /// open endpoints, otherwise the wire name of the lowest level that may call it (<c>public</c> means any
    /// signed-in account).
    /// </summary>
    public static readonly (string Method, string Template, string Url, string MinLevel)[] All =
    [
        ("POST", "api/v1/auth/tokens", "/api/v1/auth/tokens", Anonymous),
        ("POST", "api/v1/auth/tokens/refresh", "/api/v1/auth/tokens/refresh", Anonymous),
        ("POST", "api/v1/auth/tokens/revocation", "/api/v1/auth/tokens/revocation", Anonymous),
        ("POST", "api/v1/auth/registrations", "/api/v1/auth/registrations", Anonymous),
        ("GET", "api/v1/auth/me", "/api/v1/auth/me", "public"),
        ("GET", ".well-known/jwks.json", "/.well-known/jwks.json", Anonymous),
        ("POST", "api/v1/users", "/api/v1/users", "administrator"),
        ("PUT", "api/v1/users/{userId:guid}/access-level", $"/api/v1/users/{Id}/access-level", "administrator"),
        ("POST", "api/v1/users/{userId:guid}/deactivation", $"/api/v1/users/{Id}/deactivation", "administrator"),
        ("GET", "api/v1/diagnostics/runtime", "/api/v1/diagnostics/runtime", "developer"),
        ("GET", "api/v1/products", "/api/v1/products", Anonymous),
        ("GET", "api/v1/products/{productId:guid}", $"/api/v1/products/{Id}", Anonymous),
        ("POST", "api/v1/products", "/api/v1/products", "collaborator"),
        ("PATCH", "api/v1/products/{productId:guid}", $"/api/v1/products/{Id}", "collaborator"),
        ("PUT", "api/v1/products/{productId:guid}/price", $"/api/v1/products/{Id}/price", "manager"),
        ("POST", "api/v1/products/{productId:guid}/activation", $"/api/v1/products/{Id}/activation", "manager"),
        (
            "POST",
            "api/v1/products/{productId:guid}/discontinuation",
            $"/api/v1/products/{Id}/discontinuation",
            "manager"
        ),
        ("DELETE", "api/v1/products/{productId:guid}", $"/api/v1/products/{Id}", "administrator"),
        ("POST", "api/v1/carts", "/api/v1/carts", "public"),
        ("GET", "api/v1/carts/{cartId:guid}", $"/api/v1/carts/{Id}", "public"),
        ("POST", "api/v1/carts/{cartId:guid}/items", $"/api/v1/carts/{Id}/items", "public"),
        ("DELETE", "api/v1/carts/{cartId:guid}/items/{itemId:guid}", $"/api/v1/carts/{Id}/items/{Id}", "public"),
        ("POST", "api/v1/carts/{cartId:guid}/checkout", $"/api/v1/carts/{Id}/checkout", "public"),
        ("GET", "api/v1/checkouts/{checkoutId:guid}", $"/api/v1/checkouts/{Id}", "public"),
        ("GET", "api/v1/orders", "/api/v1/orders", "public"),
        ("GET", "api/v1/orders/{orderId:guid}", $"/api/v1/orders/{Id}", "public"),
        ("POST", "api/v1/orders/{orderId:guid}/cancellation", $"/api/v1/orders/{Id}/cancellation", "public"),
        ("GET", "api/v1/payments/{paymentId:guid}", $"/api/v1/payments/{Id}", "public"),
        ("POST", "api/v1/payments/{paymentId:guid}/refunds", $"/api/v1/payments/{Id}/refunds", "manager"),
        (
            "POST",
            "api/v1/payments/webhooks/{provider:regex(^[a-z0-9-]{{2,32}}$)}",
            "/api/v1/payments/webhooks/example-provider",
            Anonymous
        ),
        ("GET", "api/v1/inventory/items/{sku}", "/api/v1/inventory/items/CAF-600-PRT", "collaborator"),
        (
            "POST",
            "api/v1/inventory/items/{sku}/adjustments",
            "/api/v1/inventory/items/CAF-600-PRT/adjustments",
            "manager"
        ),
        ("GET", "api/v1/orders/{orderId:guid}/shipments", $"/api/v1/orders/{Id}/shipments", "public"),
        ("GET", "api/v1/customers/me", "/api/v1/customers/me", "public"),
    ];

    /// <summary>Theory data: method and URL of every endpoint that requires authentication.</summary>
    public static TheoryData<string, string> Protected()
    {
        var data = new TheoryData<string, string>();
        foreach (
            var (method, _, url, _) in All.Where(endpoint =>
                !string.Equals(endpoint.MinLevel, Anonymous, StringComparison.Ordinal)
            )
        )
        {
            data.Add(method, url);
        }

        return data;
    }

    /// <summary>
    /// Theory data for the authorization matrix: every protected endpoint against every principal
    /// (anonymous and the five levels).
    /// </summary>
    public static TheoryData<string, string, string, string> Matrix()
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (
            var (method, _, url, minLevel) in All.Where(endpoint =>
                !string.Equals(endpoint.MinLevel, Anonymous, StringComparison.Ordinal)
            )
        )
        {
            foreach (var principal in new[] { Anonymous }.Concat(TestAccounts.AllLevels))
            {
                data.Add(method, url, minLevel, principal);
            }
        }

        return data;
    }
}
