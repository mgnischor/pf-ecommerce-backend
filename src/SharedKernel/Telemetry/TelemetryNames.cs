namespace Portfolio.SharedKernel.Telemetry;

/// <summary>
/// The names every signal uses (ai/OBSERVABILITY.md §3.4, §5.2): one place, so a source or meter that is not registered
/// with the SDK cannot silently drop data, and so dashboards and alerts query names that are stable. Neutral on purpose:
/// it holds constants only, so the API and the Infrastructure layers can share it without depending on each other. The
/// Domain layer has no telemetry and never uses it.
/// </summary>
internal static class TelemetryNames
{
    /// <summary>Common prefix of every custom <c>ActivitySource</c> and <c>Meter</c>; the SDK registers <c>Ecommerce.*</c>.</summary>
    public const string Prefix = "Ecommerce";

    /// <summary>Pattern the SDK uses to subscribe to every custom meter.</summary>
    public const string MeterPattern = Prefix + ".*";

    /// <summary>Activity source of the cache boundary.</summary>
    public const string CacheSource = Prefix + ".Cache";

    /// <summary>Activity source of the messaging adapter (the outbox relay and, later, the consumers).</summary>
    public const string MessagingSource = Prefix + ".Messaging";

    /// <summary>Meter of the cache boundary.</summary>
    public const string CacheMeter = Prefix + ".Cache";

    /// <summary>Meter of the outbox.</summary>
    public const string OutboxMeter = Prefix + ".Outbox";

    /// <summary>Meter that carries the business KPIs of a bounded context.</summary>
    /// <param name="context">Name of the context in PascalCase (<c>Inventory</c>).</param>
    public static string ContextMeter(string context) => $"{Prefix}.{context}";

    /// <summary>Attribute: the bounded context a signal belongs to (<c>inventory</c>). Low cardinality.</summary>
    public const string BoundedContext = "app.bounded_context";

    /// <summary>Attribute: the opaque identifier of the authenticated account. Never a name or an e-mail.</summary>
    public const string ActorId = "app.actor.id";

    /// <summary>Attribute: <c>user</c> or <c>service</c>.</summary>
    public const string ActorType = "app.actor.type";

    /// <summary>Attribute: <c>rejected</c> for an expected business refusal, which is not an error.</summary>
    public const string Outcome = "app.outcome";

    /// <summary>Attribute: logical name of a cache entry (<c>identity.account</c>). Bounded set.</summary>
    public const string CacheName = "app.cache.name";

    /// <summary>Attribute: the outcome of one cache request (<c>hit</c>, <c>miss</c>, <c>error</c>).</summary>
    public const string CacheOutcome = "app.cache.outcome";

    /// <summary>Attribute: the cache operation (<c>get_or_create</c>, <c>remove</c>).</summary>
    public const string CacheOperation = "app.cache.operation";
}
