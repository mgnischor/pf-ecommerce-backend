namespace Portfolio.SharedKernel.API;

/// <summary>Names of the request headers that are part of the API contract (ai/API_CONTRACTS.md §3 and §11).</summary>
public static class ApiHeaders
{
    /// <summary>Client-generated key that makes a retried unsafe request idempotent.</summary>
    public const string IdempotencyKey = "Idempotency-Key";

    /// <summary>ETag of the resource version the client acted on (precondition).</summary>
    public const string IfMatch = "If-Match";

    /// <summary>Minimum length of an idempotency key.</summary>
    public const int IdempotencyKeyMinLength = 8;

    /// <summary>Maximum length of an idempotency key.</summary>
    public const int IdempotencyKeyMaxLength = 64;
}
