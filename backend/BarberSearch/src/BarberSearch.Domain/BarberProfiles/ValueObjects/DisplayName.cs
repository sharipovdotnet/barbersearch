using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.BarberProfiles.ValueObjects;

public class DisplayName
{
    public string Value { get; }
    
    private DisplayName(string value) => Value = value;

    public static Result<DisplayName, Error> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Error.Validation(
                "barberProfile.displayName",
                $"Barber profile cannot be empty.");
        }

        return new DisplayName(value);
    }
}