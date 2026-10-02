using System.Text.Json;
using System.Text.Json.Serialization;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// The JSON of integration-event bodies, written by <see cref="OutboxMessage"/> and read by consumers: camelCase
/// properties, camelCase string enums, money as an object with a decimal-string amount (ai/API_CONTRACTS.md §3). The JSON
/// Schemas under <c>docs/events</c> describe exactly this, and a contract test keeps them honest.
/// </summary>
internal static class MessageJson
{
    /// <summary>Web defaults plus the converters above; unknown properties are ignored when reading.</summary>
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new MoneyJsonConverter());
        return options;
    }
}
