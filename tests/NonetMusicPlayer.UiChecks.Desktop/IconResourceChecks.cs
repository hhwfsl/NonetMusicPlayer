using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal static class IconResourceChecks
{
    internal static void Run(string exe, string ico)
    {
        if (!OperatingSystem.IsWindows()) return;
        var bytes = File.ReadAllBytes(ico);
        var expected = Enumerable.Range(0, BitConverter.ToUInt16(bytes, 4)).Select(i =>
        {
            var offset = 6 + i * 16;
            return Convert.ToHexString(SHA256.HashData(bytes.AsSpan(BitConverter.ToInt32(bytes, offset + 12), BitConverter.ToInt32(bytes, offset + 8))));
        }).ToHashSet();
        var library = LoadLibraryEx(Path.GetFullPath(exe), 0, 0x22);
        if (library == 0) throw new InvalidOperationException("Cannot read native executable resources");
        var groups = new List<nint>();
        EnumResourceNamesCallback callback = (_, _, name, _) => { groups.Add(name); return true; };
        try
        {
            if (!EnumResourceNames(library, 14, callback, 0) || groups.Count == 0) throw new InvalidOperationException("EXE has no icon groups");
            foreach (var name in groups)
            {
                var group = ReadResource(library, 14, name);
                var count = BitConverter.ToUInt16(group, 4);
                for (var index = 0; index < count; index++)
                {
                    var entry = 6 + index * 14;
                    var resource = ReadResource(library, 3, BitConverter.ToUInt16(group, entry + 12));
                    var hash = Convert.ToHexString(SHA256.HashData(resource));
                    if (!expected.Contains(hash)) throw new InvalidOperationException($"Old/mismatched native icon: group {name}, entry {index}, size {resource.Length}");
                }
                Console.WriteLine($"PASS native icon group {name}: all {count} resolutions match current ICO exactly");
            }
        }
        finally { FreeLibrary(library); GC.KeepAlive(callback); }
    }
    private static byte[] ReadResource(nint module, nint type, nint name)
    {
        var resource = FindResource(module, name, type); var size = checked((int)SizeofResource(module, resource));
        var result = new byte[size]; Marshal.Copy(LockResource(LoadResource(module, resource)), result, 0, size); return result;
    }
    private delegate bool EnumResourceNamesCallback(nint module, nint type, nint name, nint parameter);
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode)] private static extern nint LoadLibraryEx(string path, nint file, uint flags);
    [DllImport("kernel32.dll", EntryPoint = "EnumResourceNamesW")] private static extern bool EnumResourceNames(nint module, nint type, EnumResourceNamesCallback callback, nint parameter);
    [DllImport("kernel32.dll", EntryPoint = "FindResourceW")] private static extern nint FindResource(nint module, nint name, nint type);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(nint module, nint resource);
    [DllImport("kernel32.dll")] private static extern nint LoadResource(nint module, nint resource);
    [DllImport("kernel32.dll")] private static extern nint LockResource(nint data);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(nint module);
}
