namespace BarberSearch.Web.Storage;

/// <summary>
/// Объект, прочитанный из хранилища: байты и сохранённый MIME-тип.
/// </summary>
/// <remarks>
/// Байты, а не поток: раздача идёт через <c>FileStreamResult</c> с поддержкой <c>Range</c>,
/// поэтому потоку нужны известная длина и seek. Файл ограничен доменной константой
/// <c>Constants.MAX_PHOTO_BYTES</c> (5 МиБ), так что буферизация одного файла на запрос
/// предсказуема; поток из SDK пришлось бы держать открытым до конца ответа.
/// </remarks>
/// <param name="Content">Содержимое объекта целиком.</param>
/// <param name="ContentType">MIME-тип из метаданных объекта.</param>
internal sealed record PhotoObject(ReadOnlyMemory<byte> Content, string ContentType);
