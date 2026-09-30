namespace Portfolio.ArchitectureTests;

/// <summary>The bounded contexts of the modular monolith (ai/ARCHITECTURE.md §2.1).</summary>
internal static class Contexts
{
    public static readonly string[] Names =
    [
        "Billing",
        "Cart",
        "Catalog",
        "Checkout",
        "Customers",
        "Identity",
        "Inventory",
        "Notifications",
        "Ordering",
        "Promotions",
        "Reviews",
        "Shipping",
    ];

    /// <summary>xUnit theory data: one row per bounded context.</summary>
    public static TheoryData<string> All()
    {
        var data = new TheoryData<string>();
        data.AddRange(Names);
        return data;
    }
}

/// <summary>Builds the namespace regular expressions used by the rules.</summary>
internal static class Namespace
{
    /// <summary>Matches the context namespace and everything below it.</summary>
    public static string Context(string context) => $@"^Portfolio\.{context}(\.|$)";

    /// <summary>Matches a layer namespace of a context and everything below it.</summary>
    public static string Layer(string context, string layer) => $@"^Portfolio\.{context}\.{layer}(\.|$)";
}
