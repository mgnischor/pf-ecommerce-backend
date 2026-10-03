using Portfolio.Customers.Application;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Customers.Infrastructure;

/// <summary>
/// Consumes Identity's <c>identity.customer-registered</c> event and creates the profile of the new customer. The queue
/// belongs to this consumer, so Customers keeps receiving events while Identity is down or redeployed.
/// </summary>
internal sealed class CustomerRegisteredConsumer : JsonMessageConsumer<CustomerRegisteredMessage>
{
    /// <inheritdoc />
    public override string Name => CreateCustomerProfileOnRegistrationHandler.ConsumerName;

    /// <inheritdoc />
    public override string BindingKey => "identity.customer-registered";

    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(
        ReceivedMessage message,
        CustomerRegisteredMessage payload,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        var handler = services.GetRequiredService<CreateCustomerProfileOnRegistrationHandler>();

        var result = await handler.HandleAsync(
            new CreateCustomerProfileOnRegistrationCommand(
                message.MessageId,
                payload.AggregateId,
                payload.Email,
                payload.FullName,
                payload.Phone,
                payload.Locale,
                payload.TimeZone
            ),
            cancellationToken
        );

        // An event without a usable account or e-mail cannot be fixed by retrying.
        if (result.IsFailure)
        {
            throw new PoisonMessageException("The event has no usable account identifier or e-mail for a profile.");
        }

        var outcome = result.Value;
        if (outcome.IgnoredFields.Count > 0)
        {
            CustomersLog.OptionalFieldsIgnored(
                services.GetRequiredService<ILogger<CustomerRegisteredConsumer>>(),
                outcome.IgnoredFields.Count,
                string.Join(", ", outcome.IgnoredFields)
            );
        }

        return outcome.Handled;
    }
}
