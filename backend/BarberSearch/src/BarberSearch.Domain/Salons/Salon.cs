using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.Salons;

/// <summary>
/// Салон — агрегатный корень. Барберы салона живут в агрегате <c>BarberProfile</c>
/// (сущность <c>BarberSalon</c>), поэтому коллекции барберов здесь нет.
/// </summary>
public class Salon : AggregateRoot
{
    public string Name { get; private set; } = null!;

    public string Address { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public string? PostalCode { get; private set; }
    public string Country { get; private set; } = null!;

    /// <summary>
    /// Идентификатор часового пояса IANA (например, <c>Europe/Moscow</c>).
    /// Без него локальное время салона не перевести в UTC и слоты не посчитать.
    /// </summary>
    public string TimeZoneId { get; private set; } = null!;

    public double Latitude { get; private set; }
    public double Longitude { get; private set; }

    public string? PhoneNumber { get; private set; }
    public string? WebsiteUrl { get; private set; }

    public bool IsActive { get; private set; }

    private Salon()
    {
    }

    public static Result<Salon, Failure> Create(
        string name,
        string address,
        string city,
        string country,
        string timeZoneId,
        double latitude,
        double longitude,
        string? postalCode = null,
        string? phoneNumber = null,
        string? websiteUrl = null)
    {
        var errors = DomainErrors.Collect();

        ValidateRequired(name, nameof(name), "salon.name", errors);
        ValidateRequired(address, nameof(address), "salon.address", errors);
        ValidateRequired(city, nameof(city), "salon.city", errors);
        ValidateRequired(country, nameof(country), "salon.country", errors);
        ValidateRequired(timeZoneId, nameof(timeZoneId), "salon.timeZoneId", errors);
        ValidateCoordinates(latitude, longitude, errors);

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new Salon
        {
            Name = name,
            Address = address,
            City = city,
            Country = country,
            TimeZoneId = timeZoneId,
            Latitude = latitude,
            Longitude = longitude,
            PostalCode = postalCode,
            PhoneNumber = phoneNumber,
            WebsiteUrl = websiteUrl,
            IsActive = true
        };
    }

    public UnitResult<Failure> UpdateLocation(
        string address,
        string city,
        string country,
        string timeZoneId,
        double latitude,
        double longitude,
        string? postalCode = null)
    {
        var errors = DomainErrors.Collect();

        ValidateRequired(address, nameof(address), "salon.address", errors);
        ValidateRequired(city, nameof(city), "salon.city", errors);
        ValidateRequired(country, nameof(country), "salon.country", errors);
        ValidateRequired(timeZoneId, nameof(timeZoneId), "salon.timeZoneId", errors);
        ValidateCoordinates(latitude, longitude, errors);

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        Address = address;
        City = city;
        Country = country;
        TimeZoneId = timeZoneId;
        Latitude = latitude;
        Longitude = longitude;
        PostalCode = postalCode;

        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> UpdateContactInfo(
        string? phoneNumber,
        string? websiteUrl)
    {
        PhoneNumber = phoneNumber;
        WebsiteUrl = websiteUrl;

        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> Activate()
    {
        if (IsActive)
        {
            return DomainErrors.Ok();
        }

        IsActive = true;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    public UnitResult<Failure> Deactivate()
    {
        if (!IsActive)
        {
            return DomainErrors.Ok();
        }

        IsActive = false;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    private static void ValidateRequired(
        string? value,
        string field,
        string code,
        List<Error> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(Error.Validation(code, "Value cannot be empty.", field));
        }
    }

    private static void ValidateCoordinates(
        double latitude,
        double longitude,
        List<Error> errors)
    {
        if (latitude is < Constants.MIN_LATITUDE or > Constants.MAX_LATITUDE)
        {
            errors.Add(Error.Validation(
                "salon.latitude",
                $"Latitude must be between {Constants.MIN_LATITUDE} and {Constants.MAX_LATITUDE}, got {latitude}.",
                nameof(latitude)));
        }

        if (longitude is < Constants.MIN_LONGITUDE or > Constants.MAX_LONGITUDE)
        {
            errors.Add(Error.Validation(
                "salon.longitude",
                $"""
                 Longitude must be between {Constants.MIN_LONGITUDE} and {Constants.MAX_LONGITUDE}, 
                 got {longitude}.
                 """,
                nameof(longitude)));
        }
    }
}
