using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace BarberSearch.Web.OpenApi;

/// <summary>
/// Добавляет в документ OpenAPI схему Bearer, чтобы в Swagger UI работала кнопка Authorize.
/// </summary>
/// <remarks>
/// Cookie-схему описать в OpenAPI нельзя: браузер сам прикладывает cookie к запросам
/// с того же origin, поэтому после <c>POST api/auth/login/cookie</c> кнопка Try it out
/// работает без дополнительных настроек.
/// </remarks>
public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    /// <summary>
    /// Имя схемы в документе.
    /// </summary>
    public const string SchemeName = "Bearer";

    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var scheme = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Access-токен из POST /api/auth/login."
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = scheme;

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeName, document)] = []
        });

        return Task.CompletedTask;
    }
}
