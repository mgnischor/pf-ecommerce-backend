namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// The mapped API surface as a contract snapshot: adding, removing, or re-routing an endpoint must be a
/// deliberate change to this table (and to <c>Portfolio.http</c>).
/// </summary>
internal static class ApiEndpoints
{
    private const string Id = "0199f3a2-7c10-7d3e-8a51-2b9d4c6e1f01";

    /// <summary>Method, route template as mapped, a concrete URL, and whether the endpoint is anonymous.</summary>
    public static readonly (string Method, string Template, string Url, bool Anonymous)[] All =
    [
        ("POST", "api/v1/auth/tokens", "/api/v1/auth/tokens", true),
        ("GET", "api/v1/products", "/api/v1/products", true),
        ("GET", "api/v1/products/{productId:guid}", $"/api/v1/products/{Id}", true),
        ("POST", "api/v1/products", "/api/v1/products", false),
        ("PATCH", "api/v1/products/{productId:guid}", $"/api/v1/products/{Id}", false),
        ("PUT", "api/v1/products/{productId:guid}/price", $"/api/v1/products/{Id}/price", false),
        ("POST", "api/v1/products/{productId:guid}/activation", $"/api/v1/products/{Id}/activation", false),
        ("POST", "api/v1/products/{productId:guid}/discontinuation", $"/api/v1/products/{Id}/discontinuation", false),
        ("DELETE", "api/v1/products/{productId:guid}", $"/api/v1/products/{Id}", false),
        ("POST", "api/v1/carts", "/api/v1/carts", false),
        ("GET", "api/v1/carts/{cartId:guid}", $"/api/v1/carts/{Id}", false),
        ("POST", "api/v1/carts/{cartId:guid}/items", $"/api/v1/carts/{Id}/items", false),
        ("DELETE", "api/v1/carts/{cartId:guid}/items/{itemId:guid}", $"/api/v1/carts/{Id}/items/{Id}", false),
        ("POST", "api/v1/carts/{cartId:guid}/checkout", $"/api/v1/carts/{Id}/checkout", false),
        ("GET", "api/v1/checkouts/{checkoutId:guid}", $"/api/v1/checkouts/{Id}", false),
        ("GET", "api/v1/orders", "/api/v1/orders", false),
        ("GET", "api/v1/orders/{orderId:guid}", $"/api/v1/orders/{Id}", false),
        ("POST", "api/v1/orders/{orderId:guid}/cancellation", $"/api/v1/orders/{Id}/cancellation", false),
        ("GET", "api/v1/payments/{paymentId:guid}", $"/api/v1/payments/{Id}", false),
        ("POST", "api/v1/payments/{paymentId:guid}/refunds", $"/api/v1/payments/{Id}/refunds", false),
        (
            "POST",
            "api/v1/payments/webhooks/{provider:regex(^[a-z0-9-]{{2,32}}$)}",
            "/api/v1/payments/webhooks/example-provider",
            true
        ),
        ("GET", "api/v1/inventory/items/{sku}", "/api/v1/inventory/items/CAF-600-PRT", false),
        ("POST", "api/v1/inventory/items/{sku}/adjustments", "/api/v1/inventory/items/CAF-600-PRT/adjustments", false),
        ("GET", "api/v1/orders/{orderId:guid}/shipments", $"/api/v1/orders/{Id}/shipments", false),
        ("GET", "api/v1/customers/me", "/api/v1/customers/me", false),
    ];

    /// <summary>Theory data: method and URL of every endpoint that requires authentication.</summary>
    public static TheoryData<string, string> Protected()
    {
        var data = new TheoryData<string, string>();
        foreach (var (method, _, url, _) in All.Where(endpoint => !endpoint.Anonymous))
        {
            data.Add(method, url);
        }

        return data;
    }
}
