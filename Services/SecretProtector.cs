using System.Runtime.InteropServices;
using System.Text;

namespace PADLOck.Services;

// Шифрование паролей средствами Windows (DPAPI): расшифровать может только тот же пользователь на том же компьютере
public static class SecretProtector
{
    private const string Prefix = "dpapi:";
    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PADLOck · vvedyaev");

    public static bool IsProtected(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Protect(string plain)
    {
        if (plain.Length == 0)
            return "";
        return Prefix + Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(plain), encrypt: true));
    }

    // Незашифрованное значение (старые настройки, стартовый профиль) возвращается как есть
    public static bool TryUnprotect(string stored, out string plain)
    {
        plain = stored;
        if (!IsProtected(stored))
            return true;
        try
        {
            plain = Encoding.UTF8.GetString(Transform(Convert.FromBase64String(stored[Prefix.Length..]), encrypt: false));
            return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicFailure)
        {
            plain = "";
            return false;
        }
    }

    private sealed class CryptographicFailure : Exception
    {
        public CryptographicFailure(int code) : base($"DPAPI error {code}") { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    private static byte[] Transform(byte[] input, bool encrypt)
    {
        // Вне Windows (тесты) — обратимая заглушка
        if (!OperatingSystem.IsWindows())
            return input.Select(b => (byte)(b ^ 0x5A)).ToArray();

        var dataIn = ToBlob(input);
        var entropy = ToBlob(Entropy);
        try
        {
            var ok = encrypt
                ? CryptProtectData(ref dataIn, "PADLOck", ref entropy, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out var dataOut)
                : CryptUnprotectData(ref dataIn, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out dataOut);
            if (!ok)
                throw new CryptographicFailure(Marshal.GetLastWin32Error());
            try
            {
                var result = new byte[dataOut.Size];
                Marshal.Copy(dataOut.Data, result, 0, dataOut.Size);
                return result;
            }
            finally
            {
                LocalFree(dataOut.Data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(dataIn.Data);
            Marshal.FreeHGlobal(entropy.Data);
        }
    }

    private static DataBlob ToBlob(byte[] bytes)
    {
        var blob = new DataBlob { Size = bytes.Length, Data = Marshal.AllocHGlobal(Math.Max(1, bytes.Length)) };
        Marshal.Copy(bytes, 0, blob.Data, bytes.Length);
        return blob;
    }
}
