using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HadalStream.Crypto;

public static class AbyssCrypto
{
    public static string Md5Hex(string value) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));

    // Abyss's player hashes numbers as one byte per decimal digit value, not as ASCII text.
    public static string Md5Hex(long value) =>
        Convert.ToHexStringLower(MD5.HashData([.. value.ToString(CultureInfo.InvariantCulture).Select(c => (byte)(c - '0'))]));

    // The ASCII hex string itself is the key (32 bytes => AES-256) and its first 16 bytes are the IV.
    public static byte[] AesCtr(ReadOnlySpan<byte> data, string hexKey)
    {
        var key = Encoding.ASCII.GetBytes(hexKey);
        return AesCtr(data, key, key.AsSpan(0, 16));
    }

    // .NET has no CTR mode; counter is a 128-bit big-endian integer like Java's AES/CTR/NoPadding.
    public static byte[] AesCtr(ReadOnlySpan<byte> data, byte[] key, ReadOnlySpan<byte> iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        Span<byte> counter = stackalloc byte[16];
        Span<byte> keystream = stackalloc byte[16];
        iv.CopyTo(counter);
        var output = new byte[data.Length];
        for (var offset = 0; offset < data.Length; offset += 16)
        {
            aes.EncryptEcb(counter, keystream, PaddingMode.None);
            for (var j = 0; j < 16 && offset + j < data.Length; j++)
                output[offset + j] = (byte)(data[offset + j] ^ keystream[j]);
            for (var k = 15; k >= 0 && ++counter[k] == 0; k--) { }
        }
        return output;
    }

    public static string DecryptMedia(string media, string hexKey) =>
        Encoding.UTF8.GetString(AesCtr([.. media.Select(c => (byte)c)], hexKey));

    public static string SegmentToken(string path, long size)
    {
        var first = Convert.ToBase64String(AesCtr(Encoding.UTF8.GetBytes(path), Md5Hex(size))).TrimEnd('=');
        return Convert.ToBase64String(Encoding.ASCII.GetBytes(first)).TrimEnd('=');
    }
}
