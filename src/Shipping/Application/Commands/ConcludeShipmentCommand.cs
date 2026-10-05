namespace Portfolio.Shipping.Application;

/// <summary>Request to record how a shipment in transit ended (BR-SHP-002): delivered, or failed with a reason.</summary>
/// <param name="ShipmentId">The shipment.</param>
/// <param name="Outcome">How the shipment ended.</param>
/// <param name="FailureReasonCode">Machine-readable reason; required for <see cref="ShipmentOutcome.Failed"/>, ignored otherwise.</param>
internal sealed record ConcludeShipmentCommand(
    Guid ShipmentId,
    ShipmentOutcome Outcome,
    string? FailureReasonCode = null
);
