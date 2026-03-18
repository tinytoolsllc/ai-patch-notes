using System.Security.Cryptography;

namespace PatchNotes.Data;

public static class IdGenerator
{
    private const string Alphabet = "_-0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const int Size = 21;

    public static string NewId()
    {
        Span<byte> bytes = stackalloc byte[Size];
        RandomNumberGenerator.Fill(bytes);
        return string.Create(Size, bytes, static (chars, b) =>
        {
            for (var i = 0; i < chars.Length; i++)
                chars[i] = Alphabet[b[i] & 63];
        });
    }
}
