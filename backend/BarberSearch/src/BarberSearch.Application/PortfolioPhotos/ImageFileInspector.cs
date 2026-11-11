using System.Buffers.Binary;
using BarberSearch.Domain;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.PortfolioPhotos;

/// <summary>
/// Проверка загруженного файла изображения по сигнатуре и заголовку.
/// </summary>
/// <remarks>
/// <para>
/// Чистая логика без I/O и без сторонних пакетов: на вход приходит <see cref="ReadOnlySpan{T}"/>
/// байтов, на выходе — формат и размеры. Полное декодирование на сервере не выполняется:
/// оно открыло бы decompression bomb и потребовало бы зависимости-декодера. Проверка
/// заголовка закрывает ровно то, что нужно до сохранения файла: тип, вес и габариты.
/// </para>
/// <para>
/// Принимаются только JPEG, PNG и WebP. SVG запрещён (XSS через <c>&lt;script&gt;</c>),
/// GIF — анимация и лишний вес. Тип определяется по сигнатуре, а не по имени или
/// <c>Content-Type</c> от клиента: расширение и MIME-тип — производные от сигнатуры.
/// </para>
/// <para>
/// Коды ошибок — часть HTTP-контракта загрузки; поле <c>file</c> совпадает с именем поля
/// формы, чтобы клиент показал текст у поля выбора файла.
/// </para>
/// </remarks>
public static class ImageFileInspector
{
    /// <summary>
    /// Минимальный вес файла: всё, что меньше, — не изображение, а мусор или обрезанный файл.
    /// </summary>
    private const int MIN_FILE_BYTES = 1024;

    /// <summary>
    /// Минимальная сторона изображения в пикселях.
    /// </summary>
    private const int MIN_DIMENSION = 300;

    /// <summary>
    /// Максимальная сторона изображения в пикселях.
    /// </summary>
    private const int MAX_DIMENSION = 6000;

    /// <summary>
    /// Максимальная площадь изображения — 24 мегапикселя.
    /// </summary>
    private const long MAX_PIXELS = 24_000_000;

    /// <summary>
    /// Имя поля формы, к которому относятся ошибки проверки файла.
    /// </summary>
    private const string FILE_FIELD = "file";

    private const string EMPTY_CODE = "barberProfile.uploadPhoto.empty";
    private const string TOO_LARGE_CODE = "barberProfile.uploadPhoto.tooLarge";
    private const string UNSUPPORTED_TYPE_CODE = "barberProfile.uploadPhoto.unsupportedType";
    private const string INVALID_IMAGE_CODE = "barberProfile.uploadPhoto.invalidImage";
    private const string DIMENSIONS_CODE = "barberProfile.uploadPhoto.dimensions";

    private const string EMPTY_MESSAGE = "The image file is empty or too small.";
    private const string UNSUPPORTED_TYPE_MESSAGE = "Only JPEG, PNG and WebP images are supported.";
    private const string INVALID_IMAGE_MESSAGE = "The image file is damaged or truncated.";

    private static readonly string TooLargeMessage =
        $"The image file cannot be larger than {Constants.MAX_PHOTO_BYTES} bytes.";

    private static readonly string DimensionsMessage =
        $"Image dimensions must be between {MIN_DIMENSION} and {MAX_DIMENSION} pixels "
        + $"and cover at most {MAX_PIXELS / 1_000_000} megapixels.";

    /// <summary>
    /// Проверяет файл и возвращает его формат и размеры.
    /// </summary>
    /// <param name="content">Содержимое файла; не должно превышать
    /// <see cref="Constants.MAX_PHOTO_BYTES"/>.</param>
    /// <returns>
    /// <see cref="ImageFileInfo"/> для корректного JPEG/PNG/WebP либо <c>Failure</c> с одним
    /// из кодов <c>barberProfile.uploadPhoto.*</c>.
    /// </returns>
    public static Result<ImageFileInfo, Failure> Inspect(ReadOnlySpan<byte> content)
    {
        if (content.Length < MIN_FILE_BYTES)
        {
            return new Failure(Error.Validation(EMPTY_CODE, EMPTY_MESSAGE, FILE_FIELD));
        }

        if (content.Length > Constants.MAX_PHOTO_BYTES)
        {
            return new Failure(Error.Validation(TOO_LARGE_CODE, TooLargeMessage, FILE_FIELD));
        }

        if (IsJpeg(content))
        {
            return ParseJpeg(content);
        }

        if (IsPng(content))
        {
            return ParsePng(content);
        }

        if (IsWebP(content))
        {
            return ParseWebP(content);
        }

        return new Failure(Error.Validation(
            UNSUPPORTED_TYPE_CODE,
            UNSUPPORTED_TYPE_MESSAGE,
            FILE_FIELD));
    }

    private static bool IsJpeg(ReadOnlySpan<byte> content)
        => content.Length >= 3
           && content[0] == 0xFF
           && content[1] == 0xD8
           && content[2] == 0xFF;

    private static bool IsPng(ReadOnlySpan<byte> content)
        => content.Length >= 8
           && content[..8].SequenceEqual(PngSignature);

    private static bool IsWebP(ReadOnlySpan<byte> content)
        => content.Length >= 12
           && content[..4].SequenceEqual("RIFF"u8)
           && content.Slice(8, 4).SequenceEqual("WEBP"u8);

    /// <summary>
    /// JPEG: идём по маркерам, пока не встретим SOF с размерами. Сегменты пропускаются
    /// по длине, поэтому EXIF и прочие APPn не мешают.
    /// </summary>
    private static Result<ImageFileInfo, Failure> ParseJpeg(ReadOnlySpan<byte> content)
    {
        var position = 2;

        while (position + 1 < content.Length)
        {
            if (content[position] != 0xFF)
            {
                return InvalidImage();
            }

            // Заполнители перед маркером — допустимая часть формата.
            while (position < content.Length && content[position] == 0xFF)
            {
                position++;
            }

            if (position >= content.Length)
            {
                return InvalidImage();
            }

            var marker = content[position];
            position++;

            // Маркеры без длины: TEM и RSTn.
            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            // EOI и SOS раньше SOF означают, что размеров в файле нет.
            if (marker == 0xD9 || marker == 0xDA)
            {
                return InvalidImage();
            }

            if (position + 2 > content.Length)
            {
                return InvalidImage();
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(content[position..]);

            if (segmentLength < 2 || position + segmentLength > content.Length)
            {
                return InvalidImage();
            }

            if (IsStartOfFrame(marker))
            {
                // payload: precision(1), height(2), width(2), …
                var data = position + 2;

                if (segmentLength < 7 || data + 5 > content.Length)
                {
                    return InvalidImage();
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(content[(data + 1)..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(content[(data + 3)..]);

                return Build(PhotoFormat.Jpeg, "image/jpeg", "jpg", width, height);
            }

            position += segmentLength;
        }

        return InvalidImage();
    }

    /// <summary>
    /// PNG: ширина и высота лежат в <c>IHDR</c> — big-endian по смещениям 16 и 20.
    /// </summary>
    private static Result<ImageFileInfo, Failure> ParsePng(ReadOnlySpan<byte> content)
    {
        // 8 (сигнатура) + 4 (длина) + 4 ("IHDR") + 13 (данные) + 4 (CRC).
        const int minimalPngLength = 33;

        if (content.Length < minimalPngLength
            || !content.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return InvalidImage();
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(content[16..]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(content[20..]);

        return Build(PhotoFormat.Png, "image/png", "png", width, height);
    }

    /// <summary>
    /// WebP: первый чанк описывает кадр. <c>VP8 </c> — lossy, <c>VP8L</c> — lossless,
    /// <c>VP8X</c> — расширенный контейнер с размером холста.
    /// </summary>
    private static Result<ImageFileInfo, Failure> ParseWebP(ReadOnlySpan<byte> content)
    {
        const int firstChunkOffset = 12;

        if (content.Length < firstChunkOffset + 8)
        {
            return InvalidImage();
        }

        var fourCc = content.Slice(firstChunkOffset, 4);
        var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(
            content[(firstChunkOffset + 4)..]);
        var payloadStart = firstChunkOffset + 8;

        if (chunkSize > (uint)(content.Length - payloadStart))
        {
            return InvalidImage();
        }

        var payload = content.Slice(payloadStart, (int)chunkSize);

        if (fourCc.SequenceEqual("VP8 "u8))
        {
            return ParseLossyWebP(payload);
        }

        if (fourCc.SequenceEqual("VP8L"u8))
        {
            return ParseLosslessWebP(payload);
        }

        if (fourCc.SequenceEqual("VP8X"u8))
        {
            return ParseExtendedWebP(payload);
        }

        return InvalidImage();
    }

    /// <summary>
    /// <c>VP8 </c>: 3 байта frame tag, стартовый код <c>9D 01 2A</c>, затем 14-битные
    /// ширина и высота little-endian.
    /// </summary>
    private static Result<ImageFileInfo, Failure> ParseLossyWebP(ReadOnlySpan<byte> payload)
    {
        const int headerLength = 10;

        if (payload.Length < headerLength
            || !payload.Slice(3, 3).SequenceEqual(StartCodeVP8))
        {
            return InvalidImage();
        }

        var width = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]) & 0x3FFF;
        var height = BinaryPrimitives.ReadUInt16LittleEndian(payload[8..]) & 0x3FFF;

        return Build(PhotoFormat.WebP, "image/webp", "webp", width, height);
    }

    /// <summary>
    /// <c>VP8L</c>: сигнатура <c>0x2F</c> и 32 бита, где 14+14 бит — ширина−1 и высота−1.
    /// </summary>
    private static Result<ImageFileInfo, Failure> ParseLosslessWebP(ReadOnlySpan<byte> payload)
    {
        const int headerLength = 5;
        const byte losslessSignature = 0x2F;
        const int dimensionMask = 0x3FFF;

        if (payload.Length < headerLength || payload[0] != losslessSignature)
        {
            return InvalidImage();
        }

        var bits = BinaryPrimitives.ReadUInt32LittleEndian(payload[1..]);
        var width = (int)(bits & dimensionMask) + 1;
        var height = (int)((bits >> 14) & dimensionMask) + 1;

        return Build(PhotoFormat.WebP, "image/webp", "webp", width, height);
    }

    /// <summary>
    /// <c>VP8X</c>: 4 байта флагов, затем ширина−1 и высота−1 холста по 24 бита little-endian.
    /// </summary>
    private static Result<ImageFileInfo, Failure> ParseExtendedWebP(ReadOnlySpan<byte> payload)
    {
        const int headerLength = 10;

        if (payload.Length < headerLength)
        {
            return InvalidImage();
        }

        var width = ReadUInt24LittleEndian(payload[4..]) + 1;
        var height = ReadUInt24LittleEndian(payload[7..]) + 1;

        return Build(PhotoFormat.WebP, "image/webp", "webp", width, height);
    }

    private static Result<ImageFileInfo, Failure> Build(
        PhotoFormat format,
        string contentType,
        string extension,
        long width,
        long height)
    {
        if (width < MIN_DIMENSION
            || height < MIN_DIMENSION
            || width > MAX_DIMENSION
            || height > MAX_DIMENSION
            || width * height > MAX_PIXELS)
        {
            return new Failure(Error.Validation(
                DIMENSIONS_CODE,
                DimensionsMessage,
                FILE_FIELD));
        }

        return new ImageFileInfo(
            format,
            contentType,
            extension,
            (int)width,
            (int)height);
    }

    private static Result<ImageFileInfo, Failure> InvalidImage()
        => new Failure(Error.Validation(
            INVALID_IMAGE_CODE,
            INVALID_IMAGE_MESSAGE,
            FILE_FIELD));

    private static bool IsStartOfFrame(byte marker)
        => marker is 0xC0 or 0xC1 or 0xC2 or 0xC3
            or 0xC5 or 0xC6 or 0xC7
            or 0xC9 or 0xCA or 0xCB
            or 0xCD or 0xCE or 0xCF;

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> value)
        => value[0] | (value[1] << 8) | (value[2] << 16);

    private static ReadOnlySpan<byte> PngSignature =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> StartCodeVP8 => [0x9D, 0x01, 0x2A];
}
