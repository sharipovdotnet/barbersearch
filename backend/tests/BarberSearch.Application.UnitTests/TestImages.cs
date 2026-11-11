using System.Buffers.Binary;
using System.Text;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Минимальные, но структурно корректные изображения для тестов проверки файла.
/// </summary>
/// <remarks>
/// Полного декодирования инспектор не делает: ему достаточно сигнатуры и заголовка,
/// поэтому «картинка» — это заголовок плюс нулевое заполнение до минимального веса.
/// Файлы меньше 1 КиБ инспектор отвергает раньше разбора заголовка.
/// </remarks>
internal static class TestImages
{
    private const int MINIMAL_SIZE = 2048;

    public static byte[] Png(int width, int height)
    {
        var content = CreateEmpty();

        PngSignature.CopyTo(content);

        BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(8), 13);
        "IHDR"u8.CopyTo(content.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(20), (uint)height);

        content[24] = 8; // глубина цвета
        content[25] = 6; // RGBA

        return content;
    }

    public static byte[] Jpeg(int width, int height)
    {
        var content = CreateEmpty();

        content[0] = 0xFF;
        content[1] = 0xD8;
        content[2] = 0xFF;
        content[3] = 0xC0;

        // Длина сегмента SOF0: 2 байта длины + precision + height + width + компоненты.
        BinaryPrimitives.WriteUInt16BigEndian(content.AsSpan(4), 17);
        content[6] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(content.AsSpan(7), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(content.AsSpan(9), (ushort)width);
        content[11] = 3;

        return content;
    }

    public static byte[] WebPLossless(int width, int height)
    {
        var content = CreateEmpty();
        var payloadLength = 5;

        WriteWebPHeader(content, "VP8L"u8, payloadLength);
        content[20] = 0x2F;

        var bits = (uint)((width - 1) | ((height - 1) << 14));
        BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(21), bits);

        return content;
    }

    public static byte[] WebPLossy(int width, int height)
    {
        var content = CreateEmpty();

        WriteWebPHeader(content, "VP8 "u8, 10);

        // Три байта frame tag, стартовый код и 14-битные размеры.
        content[20 + 3] = 0x9D;
        content[20 + 4] = 0x01;
        content[20 + 5] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(content.AsSpan(20 + 6), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(content.AsSpan(20 + 8), (ushort)height);

        return content;
    }

    public static byte[] WebPExtended(int width, int height)
    {
        var content = CreateEmpty();

        WriteWebPHeader(content, "VP8X"u8, 10);

        WriteUInt24LittleEndian(content.AsSpan(20 + 4), width - 1);
        WriteUInt24LittleEndian(content.AsSpan(20 + 7), height - 1);

        return content;
    }

    /// <summary>
    /// JPEG с сигнатурой и обрезанным сегментом SOF: разбор не должен вычитать за границу.
    /// </summary>
    public static byte[] TruncatedJpeg()
    {
        var content = CreateEmpty();

        content[0] = 0xFF;
        content[1] = 0xD8;
        content[2] = 0xFF;
        content[3] = 0xE0;

        // APP0 съедает файл до последних шести байт, где начинается SOF с длиной 17:
        // данных сегмента уже не хватает, разбор обязан вернуть invalidImage.
        BinaryPrimitives.WriteUInt16BigEndian(content.AsSpan(4), 2038);
        content[2042] = 0xFF;
        content[2043] = 0xC0;
        BinaryPrimitives.WriteUInt16BigEndian(content.AsSpan(2044), 17);

        return content;
    }

    public static byte[] WithBadSignature()
    {
        var content = CreateEmpty();
        content[0] = 0x00;

        return content;
    }

    public static byte[] Svg()
        => Fill(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"800\" height=\"600\">"
            + "<script>alert(1)</script></svg>");

    public static byte[] Gif()
        => Fill("GIF89a");

    public static byte[] SmallerThanMinimalFile()
        => new byte[100];

    public static byte[] LargerThanLimit()
        => new byte[BarberSearch.Domain.Constants.MAX_PHOTO_BYTES + 1];

    private static byte[] Fill(string header)
    {
        var content = CreateEmpty();
        Encoding.UTF8.GetBytes(header).CopyTo(content, 0);

        return content;
    }

    private static byte[] CreateEmpty() => new byte[MINIMAL_SIZE];

    private static void WriteWebPHeader(byte[] content, ReadOnlySpan<byte> fourCc, int payloadLength)
    {
        "RIFF"u8.CopyTo(content);
        BinaryPrimitives.WriteUInt32LittleEndian(
            content.AsSpan(4),
            (uint)(4 + 8 + payloadLength));
        "WEBP"u8.CopyTo(content.AsSpan(8));
        fourCc.CopyTo(content.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(16), (uint)payloadLength);
    }

    private static void WriteUInt24LittleEndian(Span<byte> destination, int value)
    {
        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)(value >> 16);
    }

    private static ReadOnlySpan<byte> PngSignature =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
}
