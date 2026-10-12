using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.BarberProfiles.ValueObjects;

public class YearsOfExperience
{
    private const int MinValue = Constants.MIN_YEARS_OF_EXPERIENCE;
    private const int MaxValue = Constants.MAX_YEARS_OF_EXPERIENCE;

    public int Value { get; }

    private YearsOfExperience(int value) => Value = value;

    public static Result<YearsOfExperience, Error> Create(int value)
    {
        if (value is < MinValue or > MaxValue)
        {
            return Error.Validation(
                "barberProfile.yearsOfExperience",
                $"Years of experience must be between {MinValue} and {MaxValue}, got {value}.");
        }

        return new YearsOfExperience(value);
    }
}