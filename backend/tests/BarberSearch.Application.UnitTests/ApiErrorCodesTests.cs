using System.Reflection;
using System.Text.RegularExpressions;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberProfiles;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Реестр <see cref="ApiErrorCodes"/> — публичный контракт кодов ошибок (R-28).
/// </summary>
/// <remarks>
/// Тест рефлексией собирает все публичные строковые константы реестра, включая вложенные
/// классы. Полные коды сохранения строятся из префикса операции и суффикса, поэтому
/// проверяется формат и стабильность имён, а не конкретный набор SQLSTATE.
/// </remarks>
public class ApiErrorCodesTests
{
    private static readonly string[] ForbiddenRepositoryMethods =
    [
        "getById",
        "getByEmail",
        "getByHash",
        "getCardById",
        "getIdempotencyKey",
        "getPaged",
        "getPage",
        "getPublishedPage",
        "getByCity",
        "getByCategory",
        "getByName",
        "emailExists",
        "getActiveByBarber",
        "hasActiveOverlap"
    ];

    private static readonly Regex PublicCodeFormat = new(
        @"^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+)+$",
        RegexOptions.CultureInvariant);

    [Fact]
    public void Constants_AreNonEmptyAndUnique()
    {
        var constants = GetAllConstants();

        Assert.NotEmpty(constants);
        Assert.All(
            constants,
            constant => Assert.False(
                string.IsNullOrWhiteSpace(constant.Value),
                $"{constant.Path} is empty."));

        var duplicates = constants
            .GroupBy(constant => constant.Value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(x => x.Path))}")
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Constants_MatchPublicCodeFormat()
    {
        Assert.All(
            GetAllConstants(),
            constant => Assert.Matches(PublicCodeFormat, constant.Value));
    }

    [Fact]
    public void Constants_DoNotContainRepositoryMethodNames()
    {
        foreach (var constant in GetAllConstants())
        {
            foreach (var method in ForbiddenRepositoryMethods)
            {
                Assert.DoesNotContain(method, constant.Value, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Домен не может ссылаться на Contracts, поэтому совпадение доменной константы
    /// с публичным реестром закреплено здесь.
    /// </summary>
    [Fact]
    public void FindSalonNotFound_MatchesDomainConstant()
        => Assert.Equal(
            ApiErrorCodes.BarberProfile.FindSalonNotFound,
            BarberProfile.FIND_SALON_NOT_FOUND);

    [Fact]
    public void OnlyInternalCode_IsInternalDatabaseError()
    {
        var internalCodes = GetAllConstants()
            .Where(constant => constant.Value.StartsWith("internal.", StringComparison.Ordinal))
            .ToList();

        var only = Assert.Single(internalCodes);
        Assert.Equal("internal.database", only.Value);
        Assert.Equal(ApiErrorCodes.InternalDatabaseError, only.Value);
    }

    private static List<(string Path, string Value)> GetAllConstants()
    {
        var constants = new List<(string Path, string Value)>();

        CollectConstants(typeof(ApiErrorCodes), prefix: string.Empty, constants);

        return constants;
    }

    private static void CollectConstants(
        Type type,
        string prefix,
        List<(string Path, string Value)> constants)
    {
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (!field.IsLiteral || field.FieldType != typeof(string))
            {
                continue;
            }

            var path = string.IsNullOrEmpty(prefix)
                ? field.Name
                : $"{prefix}.{field.Name}";

            constants.Add((path, (string)field.GetRawConstantValue()!));
        }

        foreach (var nestedType in type.GetNestedTypes(BindingFlags.Public))
        {
            var nestedPrefix = string.IsNullOrEmpty(prefix)
                ? nestedType.Name
                : $"{prefix}.{nestedType.Name}";

            CollectConstants(nestedType, nestedPrefix, constants);
        }
    }
}
