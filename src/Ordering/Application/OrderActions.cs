namespace Portfolio.Ordering.Application;

/// <summary>Names of the actions an order's lifecycle can allow, as exposed in <c>allowedActions</c>.</summary>
internal static class OrderActions
{
    /// <summary>Cancel the order (BR-ORD-004).</summary>
    public const string Cancel = "cancel";
}
