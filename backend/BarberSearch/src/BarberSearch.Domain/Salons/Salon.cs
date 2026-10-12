using BarberSearch.Domain.BarberSalons;
using BarberSearch.Domain.Common;

namespace BarberSearch.Domain.Salons;

public class Salon : AggregateRoot
{
    private readonly List<BarberSalon> _barbers = new();

    public string Name { get; private set; } = null!;

    public string Address { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public string? PostalCode { get; private set; }
    public string Country { get; private set; } = null!;

    public decimal Latitude { get; private set; }
    public decimal Longitude { get; private set; }

    public string? PhoneNumber { get; private set; }
    public string? WebsiteUrl { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<BarberSalon> Barbers
        => _barbers.AsReadOnly();

    private Salon()
    {
    }

    public Salon(
        string name,
        string address,
        string city,
        string country,
        decimal latitude,
        decimal longitude)
    {
        Name = name;
        Address = address;
        City = city;
        Country = country;

        Latitude = latitude;
        Longitude = longitude;

        IsActive = true;
    }

    public void UpdateLocation(
        string address,
        string city,
        string? postalCode,
        decimal latitude,
        decimal longitude)
    {
        Address = address;
        City = city;
        PostalCode = postalCode;

        Latitude = latitude;
        Longitude = longitude;

        MarkUpdated();
    }

    public void UpdateContactInfo(
        string? phoneNumber,
        string? websiteUrl)
    {
        PhoneNumber = phoneNumber;
        WebsiteUrl = websiteUrl;

        MarkUpdated();
    }
}