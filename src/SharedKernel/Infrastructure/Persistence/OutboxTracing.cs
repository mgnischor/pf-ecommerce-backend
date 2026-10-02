using System.Diagnostics;
using Portfolio.SharedKernel.Telemetry;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>The activity source of the outbox relays; one per process, not one per closed generic relay type.</summary>
internal static class OutboxTracing
{
    /// <summary>Source of the <c>PRODUCER</c> spans, registered with the SDK as <c>Ecommerce.Messaging</c>.</summary>
    public static readonly ActivitySource Source = new(TelemetryNames.MessagingSource);
}
