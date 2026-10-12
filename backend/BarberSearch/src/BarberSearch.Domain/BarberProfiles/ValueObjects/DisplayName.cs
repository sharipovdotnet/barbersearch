using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.BarberProfiles.ValueObjects;

public class DisplayName
{
    public const int MAX_LENGTH = 120;

    public string Value { get; }

    private DisplayName(string value) => Value = value;

    public static Result<DisplayName, Error> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Error.Validation(
                "barberProfile.displayName",
                "Barber profile display name cannot be empty.",
                nameof(value));
        }

        if (value.Length > MAX_LENGTH)
        {
            return Error.Validation(
                "barberProfile.displayName.tooLong",
                $"Barber profile display name cannot be longer than {MAX_LENGTH} characters.",
                nameof(value));
        }

        return new DisplayName(value);
    }
}
