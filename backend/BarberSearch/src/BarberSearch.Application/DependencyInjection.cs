using BarberSearch.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace BarberSearch.Application;

/// <summary>
/// Регистрация обработчиков слоя Application.
/// </summary>
/// <remarks>
/// Сборка сканируется, а не перечисляется вручную: новый обработчик достаточно добавить классом,
/// реализующим <c>ICommandHandler&lt;TCommand&gt;</c>, <c>ICommandHandler&lt;TResponse, TCommand&gt;</c>
/// или <c>IQueryHandler&lt;TResponse, TQuery&gt;</c>. Время жизни — scoped: обработчик работает через
/// репозитории, а они живут в рамках одного запроса.
/// </remarks>
public static class DependencyInjection
{
    /// <summary>
    /// Регистрирует все обработчики команд и запросов сборки <c>BarberSearch.Application</c>.
    /// </summary>
    /// <param name="services">Коллекция сервисов приложения.</param>
    /// <returns>Та же коллекция сервисов — для цепочки вызовов.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.Scan(scan => scan.FromAssemblies([assembly])
            .AddClasses(classes => classes
                .AssignableToAny(
                    typeof(ICommandHandler<,>),
                    typeof(ICommandHandler<>),
                    typeof(IQueryHandler<,>)))
            .AsSelfWithInterfaces()
            .WithScopedLifetime());

        return services;
    }
}
