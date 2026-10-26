using Xunit;

// Все интеграционные тесты делят одну базу (и один контейнер): xUnit не должен запускать
// их параллельно, иначе TRUNCATE одного сценария попадёт в середину другого.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
