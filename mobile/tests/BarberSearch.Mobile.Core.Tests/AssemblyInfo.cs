using Xunit;

// Тесты меняют модульный Loc (язык интерфейса) — как и приложение. Параллельный запуск
// сделал бы результат зависимым от соседнего теста, поэтому классы идут последовательно.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
