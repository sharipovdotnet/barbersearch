namespace BarberSearch.Web.Storage;

/// <summary>
/// Провайдер хранилища файлов изображений: локальный диск или S3-совместимый сервис.
/// </summary>
/// <remarks>
/// <para>
/// Значение читается из <c>Storage:Provider</c> строкой, а не числом: числовой биндинг принял бы
/// <c>"1"</c> за <see cref="S3"/> и молча включил object storage там, где ожидался диск.
/// Допустимы <c>Local</c> и <c>S3</c> в любом регистре; по умолчанию — <see cref="Local"/>,
/// потому что dev, unit- и интеграционные тесты не должны требовать MinIO.
/// </para>
/// <para>
/// Оба провайдера реализуют один <see cref="Application.PortfolioPhotos.IPhotoStorage"/>
/// и пишут файлы по одной раскладке ключей: публичные URL и <c>?size=</c> от провайдера
/// не зависят.
/// </para>
/// </remarks>
public enum PhotoStorageProvider
{
    /// <summary>
    /// Локальный каталог <see cref="PhotoStorageOptions.RootPath"/>.
    /// </summary>
    Local = 0,

    /// <summary>
    /// S3-совместимое хранилище: AWS S3 или MinIO через <see cref="S3StorageOptions"/>.
    /// </summary>
    S3 = 1
}
