using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Plugins;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>独立于插件安装包的配置存储；更新或断开插件连接不丢失配置。</summary>
public static class PluginConfigurationStore
{
    public static string PathFor(string pluginsFolder, PluginManifest plugin)
    {
        plugin.Validate();
        var folder = Path.Combine(pluginsFolder, "Configurations");
        DataDirectoryService.RejectLinkedAncestors(folder);
        return Path.Combine(folder, plugin.Id + ".json");
    }
    public static string? Read(string pluginsFolder, PluginManifest plugin)
    {
        var path = PathFor(pluginsFolder, plugin);
        if (!File.Exists(path)) return null;
        DataDirectoryService.RejectLinkedAncestors(path);
        if (new FileInfo(path).Length > 1_000_000) throw new InvalidDataException("Plugin configuration is too large.");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var bytes = Convert.FromBase64String(root["protectedConfiguration"]!.GetValue<string>());
        var decoded = Protect(bytes, false, pluginsFolder, root["protection"]!.GetValue<string>());
        try { return Encoding.UTF8.GetString(decoded); }
        finally { CryptographicOperations.ZeroMemory(decoded); }
    }
    public static void Write(string pluginsFolder, PluginManifest plugin, string full, string publicValues)
    {
        var path = PathFor(pluginsFolder, plugin);
        var folder = Path.GetDirectoryName(path)!; Directory.CreateDirectory(folder);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var bytes = Encoding.UTF8.GetBytes(full);
        try
        {
            var protection = OperatingSystem.IsWindows() ? "windows-user-dpapi" : "local-user-aes-gcm";
            var root = new JsonObject { ["schemaVersion"] = 1, ["values"] = JsonNode.Parse(publicValues),
                ["protection"] = protection, ["protectedConfiguration"] = Convert.ToBase64String(Protect(bytes, true, pluginsFolder, protection)) };
            // 不产生含凭据的明文备份；JSON 中只有公开值和加密数据。
            AppStorage.AtomicWrite(path, root.ToJsonString(), false);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public static void Delete(string pluginsFolder, PluginManifest plugin)
    {
        var path = PathFor(pluginsFolder, plugin); DataDirectoryService.RejectLinkedAncestors(path);
        if (File.Exists(path)) File.Delete(path);
    }
    private static byte[] Protect(byte[] input, bool encrypt, string pluginsFolder, string protection)
    {
        if (protection == "windows-user-dpapi")
        {
            if (!OperatingSystem.IsWindows()) throw new CryptographicException("This configuration belongs to a Windows account.");
            var blob = new Blob { Length = input.Length, Data = Marshal.AllocHGlobal(input.Length) };
            try
            {
                Marshal.Copy(input, 0, blob.Data, input.Length);
                var ok = encrypt ? CryptProtectData(ref blob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out var output)
                    : CryptUnprotectData(ref blob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
                if (!ok) throw new CryptographicException("Cannot protect plugin configuration.");
                try { var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result; }
                finally { LocalFree(output.Data); }
            }
            finally { Marshal.Copy(new byte[input.Length], 0, blob.Data, input.Length); Marshal.FreeHGlobal(blob.Data); }
        }
        if (OperatingSystem.IsWindows()) throw new CryptographicException("This configuration belongs to another platform.");
        if (protection != "local-user-aes-gcm") throw new CryptographicException("Unknown configuration protection.");
        var keyPath = Path.Combine(pluginsFolder, "Configurations", ".key");
        DataDirectoryService.RejectLinkedAncestors(keyPath);
        if (!File.Exists(keyPath))
        {
            if (!encrypt) throw new CryptographicException("Configuration key is missing.");
            using var stream = new FileStream(keyPath, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
            var created = RandomNumberGenerator.GetBytes(32); try { stream.Write(created); } finally { CryptographicOperations.ZeroMemory(created); }
        }
        var key = File.ReadAllBytes(keyPath);
        try
        {
            using var aes = new AesGcm(key, 16);
            if (encrypt)
            {
                var result = new byte[28 + input.Length]; RandomNumberGenerator.Fill(result.AsSpan(0, 12));
                aes.Encrypt(result.AsSpan(0, 12), input, result.AsSpan(28), result.AsSpan(12, 16)); return result;
            }
            if (input.Length < 28) throw new CryptographicException("Invalid protected configuration.");
            var plain = new byte[input.Length - 28]; aes.Decrypt(input.AsSpan(0, 12), input.AsSpan(28), input.AsSpan(12, 16), plain); return plain;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
}
