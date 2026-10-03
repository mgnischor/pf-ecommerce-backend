using Portfolio.Customers.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Customers.Application;

/// <summary>
/// Changes the authenticated customer's profile (BR-CUS-001 to BR-CUS-005, BR-CUS-008). The precondition is checked
/// before the data so a stale client learns it must re-read first, and nothing is written unless every member of the
/// request is valid. Idempotent: repeating the same patch changes nothing, and the version only moves on a real change.
/// </summary>
internal sealed class UpdateCustomerProfileHandler(
    ICustomerProfileRepository profiles,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider
)
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">Validated request data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The profile after the update, or the violated rule.</returns>
    public async Task<Result<CustomerProfileView>> HandleAsync(
        UpdateCustomerProfileCommand command,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(command);

        var profile = await profiles.GetByIdAsync(command.AccountId, cancellationToken);
        if (profile is null)
        {
            return Result<CustomerProfileView>.Failure(CustomerErrors.ProfileNotFound);
        }

        if (command.ExpectedVersion != profile.Version)
        {
            return Result<CustomerProfileView>.Failure(CustomerErrors.VersionMismatch);
        }

        var fullName = Parse(command.FullName, FullName.Create);
        var phone = ParseOptional(command.Phone, PhoneNumber.Create);
        var locale = Parse(command.Locale, CustomerLocale.Create);
        var timeZone = Parse(command.TimeZone, CustomerTimeZone.Create);

        var error = fullName.Error ?? phone.Error ?? locale.Error ?? timeZone.Error;
        if (error is not null)
        {
            return Result<CustomerProfileView>.Failure(error);
        }

        if (profile.Update(fullName.Value, phone.Value, locale.Value, timeZone.Value, timeProvider))
        {
            profiles.Update(profile);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<CustomerProfileView>.Success(profile.ToView());
    }

    // A member that must always have a value: sending it empty is a violation, not a removal.
    private static Result<Change<T>> Parse<T>(Change<string?> change, Func<string?, Result<T>> create)
        where T : class
    {
        if (!change.IsSet)
        {
            return Result<Change<T>>.Success(Change<T>.Unchanged);
        }

        var parsed = create(change.Value);
        return parsed.IsFailure
            ? Result<Change<T>>.Failure(parsed.Error)
            : Result<Change<T>>.Success(Change<T>.To(parsed.Value));
    }

    // A member that may be absent: sending it empty removes it.
    private static Result<Change<T>> ParseOptional<T>(Change<string?> change, Func<string?, Result<T>> create)
        where T : class =>
        change is { IsSet: true } && string.IsNullOrWhiteSpace(change.Value)
            ? Result<Change<T>>.Success(Change<T>.To(null))
            : Parse(change, create);
}
