using Portfolio.SharedKernel.Domain;

namespace Portfolio.Shipping.Domain;

/// <summary>
/// The Shipping context's own record of whose an order is (BR-SHP-004): the order identifier, its owner, and its number.
/// It lets the context answer "the shipments of this order" for the owner and "not found" for anyone else, including
/// when the order has no shipment yet, without asking the Ordering context. Written only from Ordering's integration
/// events; never changed afterwards, since an order never changes hands.
/// </summary>
internal sealed class OrderReference : Entity
{
    /// <summary>The customer that owns the order.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>Human-readable order number.</summary>
    public string Number { get; private set; }

    /// <summary>EF Core constructor. Do not use in domain code.</summary>
    // Justification for CS8618 suppression: properties are populated by EF Core materialization.
#pragma warning disable CS8618
    private OrderReference() { }
#pragma warning restore CS8618

    private OrderReference(Guid orderId, Guid customerId, string number, TimeProvider timeProvider)
        : base(orderId, timeProvider)
    {
        CustomerId = customerId;
        Number = number;
    }

    /// <summary>Records an order and its owner.</summary>
    /// <param name="orderId">The order identifier, which this record reuses.</param>
    /// <param name="customerId">The order's owner.</param>
    /// <param name="number">The order number.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public static Result<OrderReference> Record(
        Guid orderId,
        Guid customerId,
        string? number,
        TimeProvider timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return orderId == Guid.Empty || customerId == Guid.Empty || string.IsNullOrWhiteSpace(number)
            ? Result<OrderReference>.Failure(ShipmentErrors.MalformedOrderEvent)
            : Result<OrderReference>.Success(new OrderReference(orderId, customerId, number.Trim(), timeProvider));
    }
}
