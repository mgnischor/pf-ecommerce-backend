using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Writes <see cref="Money"/> as <c>{ "amount": "25.90", "currency": "BRL" }</c> (ai/API_CONTRACTS.md §3): the amount is a
/// decimal string, never a JSON number, so no consumer ever rounds it through a binary float. Two to four decimals, which
/// is the stored scale.
/// </summary>
internal sealed class MoneyJsonConverter : JsonConverter<Money>
{
    private const string AmountFormat = "0.00##";

    /// <inheritdoc />
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Money must be an object.");
        }

        decimal? amount = null;
        string? currency = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString();
            reader.Read();

            if (string.Equals(name, "amount", StringComparison.OrdinalIgnoreCase))
            {
                amount =
                    reader.TokenType == JsonTokenType.String
                    && decimal.TryParse(
                        reader.GetString(),
                        NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out var parsed
                    )
                        ? parsed
                        : throw new JsonException("Money.amount must be a decimal string.");
            }
            else if (string.Equals(name, "currency", StringComparison.OrdinalIgnoreCase))
            {
                currency = reader.GetString();
            }
            else
            {
                reader.Skip();
            }
        }

        var money = amount is { } value ? Money.Create(value, currency) : null;
        return money is { IsSuccess: true } ? money.Value : throw new JsonException("Money is incomplete or invalid.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        writer.WriteString("amount", value.Amount.ToString(AmountFormat, CultureInfo.InvariantCulture));
        writer.WriteString("currency", value.Currency);
        writer.WriteEndObject();
    }
}
