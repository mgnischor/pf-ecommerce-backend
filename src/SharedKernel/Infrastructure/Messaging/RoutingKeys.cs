using System.Text;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// The AMQP routing key of an integration event: <c>{context}.{event-name}</c> in kebab case, such as
/// <c>catalog.product-created</c>. The key is part of the published contract, so it is derived from the namespace
/// and name of the event, never from the CLR full name.
/// </summary>
internal static class RoutingKeys
{
    /// <summary>Used as the context when the type name does not follow <c>Portfolio.{Context}.…</c>.</summary>
    public const string UnknownContext = "events";

    private const string RootNamespace = "Portfolio";

    /// <summary>Derives the routing key from a full type name.</summary>
    /// <param name="typeName">Full name of the event type (<see cref="OutboxMessage.Type"/>).</param>
    public static string For(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);

        var parts = typeName.Split('.');
        var context =
            parts.Length >= 3 && string.Equals(parts[0], RootNamespace, StringComparison.Ordinal)
                ? Kebab(parts[1])
                : UnknownContext;

        return $"{context}.{Kebab(parts[^1])}";
    }

    private static string Kebab(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsUpper(character))
            {
                var startsWord = index > 0 && (!char.IsUpper(value[index - 1]) || NextIsLower(value, index));
                if (startsWord)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static bool NextIsLower(string value, int index) =>
        index + 1 < value.Length && char.IsLower(value[index + 1]);
}
