using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.IO.Compression;

namespace Harmonia.Suite;

public sealed record ReleaseFile(string Path, string Sha256, long Size);
public sealed record SuiteManifest(int Schema, string Channel, string Version, string ServiceVersion,
    string DriverVersion, string AppServiceMinimum, string AppServiceMaximum,
    string ServiceDriverMinimum, string ServiceDriverMaximum, bool UpdateService, bool UpdateDriver,
    int TransportVersion, ReleaseFile[] Files) {
    public void Validate() {
        if (Schema != 1 || Channel != "suite" || TransportVersion != 1 || Files is null || Files.Length is < 1 or > 1000)
            throw new InvalidDataException("Unsupported Suite release manifest.");
        foreach (string version in new[] { Version, ServiceVersion, DriverVersion, AppServiceMinimum, AppServiceMaximum, ServiceDriverMinimum, ServiceDriverMaximum })
            if (!System.Version.TryParse(version, out _)) throw new InvalidDataException("Invalid release version.");
        Compatible(ServiceVersion, DriverVersion);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Files) {
            SafeRelative(file.Path);
            if (!names.Add(file.Path) || file.Size < 0 || file.Size > 256 * 1024 * 1024 ||
                file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid/duplicate release file.");
            if (!new[] { "App/", "Service/", "Driver/", "Installer/" }.Any(p => file.Path.StartsWith(p, StringComparison.Ordinal)))
                throw new InvalidDataException("Unknown release component.");
            if (new[] { ".cer", ".pfx", ".p12", ".ps1", ".bat", ".cmd", ".vbs", ".wsf" }.Contains(Path.GetExtension(file.Path).ToLowerInvariant()))
                throw new InvalidDataException("Certificates and installation scripts cannot be release payloads.");
        }
        foreach (var required in new[] { "App/Harmonia.exe", "Installer/Harmonia.Suite.Installer.exe" })
            if (!names.Contains(required)) throw new InvalidDataException("Missing " + required);
        if (UpdateService && !names.Contains("Service/HarmoniaAudioTransport.exe")) throw new InvalidDataException("Missing service.");
        if (UpdateDriver) foreach (var name in new[] { "HarmoniaVirtualAudio.inf", "HarmoniaVirtualAudio.sys", "harmoniavirtualaudio.cat" })
            if (!names.Contains("Driver/" + name)) throw new InvalidDataException("Missing driver file.");
    }
    public void Compatible(string service, string driver) {
        if (System.Version.Parse(service) < System.Version.Parse(AppServiceMinimum) || System.Version.Parse(service) > System.Version.Parse(AppServiceMaximum) ||
            System.Version.Parse(driver) < System.Version.Parse(ServiceDriverMinimum) || System.Version.Parse(driver) > System.Version.Parse(ServiceDriverMaximum))
            throw new InvalidDataException("Application/service/driver versions are incompatible.");
    }
    public static void SafeRelative(string path) {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains(':') || path.StartsWith('/') ||
            path.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ')) ||
            path.Any(c => c < 32 || "<>\"|?*".Contains(c))) throw new InvalidDataException("Unsafe release path.");
    }
}

public static class ReleaseSecurity {
    public static string Publisher => typeof(ReleaseSecurity).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "SuitePublisherSha256").Value ?? "";
    public static string DriverPublisher => typeof(ReleaseSecurity).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "SuiteDriverPublisherSha256").Value ?? "";
    public static void RequirePublisher() {
        if (Publisher.Length != 64 || !Publisher.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Suite updates are disabled: this build has no pinned production publisher. Obtain a signed Suite distribution from the project publisher.");
    }
    public static SuiteManifest ReadManifest(string directory) {
        RequirePublisher(); NoLinks(directory);
        string manifestPath = Path.Combine(directory, "suite-manifest.json");
        string signaturePath = Path.Combine(directory, "suite-manifest.p7s");
        if (new FileInfo(manifestPath).Length > 1024 * 1024 || new FileInfo(signaturePath).Length > 256 * 1024) throw new InvalidDataException("Manifest too large.");
        var bytes = File.ReadAllBytes(manifestPath);
        var cms = new SignedCms(new ContentInfo(bytes), true); cms.Decode(File.ReadAllBytes(signaturePath));
        cms.CheckSignature(false);
        if (cms.SignerInfos.Count != 1 || cms.SignerInfos[0].Certificate is not { } signer ||
            !signer.GetCertHashString(HashAlgorithmName.SHA256).Equals(Publisher, StringComparison.OrdinalIgnoreCase) ||
            !signer.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(e => e.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == "1.3.6.1.5.5.7.3.3")))
            throw new CryptographicException("Release publisher mismatch or missing code-signing usage.");
        if (cms.SignerInfos[0].DigestAlgorithm.Value is not ("2.16.840.1.101.3.4.2.1" or "2.16.840.1.101.3.4.2.2" or "2.16.840.1.101.3.4.2.3"))
            throw new CryptographicException("Weak release signature digest.");
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.3"));
        chain.ChainPolicy.ExtraStore.AddRange(cms.Certificates);
        if (!chain.Build(signer)) throw new CryptographicException("Release certificate chain/revocation verification failed.");
        var manifest = JsonSerializer.Deserialize<SuiteManifest>(bytes) ?? throw new InvalidDataException("Empty manifest.");
        manifest.Validate(); return manifest;
    }
    public static SuiteManifest VerifyDirectory(string directory) {
        var manifest = ReadManifest(directory);
        foreach (var file in manifest.Files) {
            string path = Path.Combine(directory, file.Path.Replace('/', Path.DirectorySeparatorChar));
            NoLinks(path);
            using var stream = File.OpenRead(path);
            if (stream.Length != file.Size || !Convert.ToHexString(SHA256.HashData(stream)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new CryptographicException("Release file hash mismatch: " + file.Path);
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".exe" || extension == ".sys" || extension == ".cat") VerifyExecutable(path, extension is ".sys" or ".cat");
        }
        return manifest;
    }
    public static void VerifyExecutable(string path, bool driver = false) {
        RequirePublisher();
        using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
        string expected = driver ? DriverPublisher : Publisher;
        if (expected.Length != 64 || !cert.GetCertHashString(HashAlgorithmName.SHA256).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new CryptographicException("Executable publisher mismatch.");
        var info = new WinTrustFile { Size = (uint)Marshal.SizeOf<WinTrustFile>(), Path = path };
        IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFile>());
        Marshal.StructureToPtr(info, pointer, false);
        var trust = new WinTrustData { Size = (uint)Marshal.SizeOf<WinTrustData>(), UI = 2, Choice = 1, File = pointer, StateAction = 1, Revocation = 1, Flags = 0x80 };
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try { if (WinVerifyTrust(new IntPtr(-1), ref action, ref trust) != 0) throw new CryptographicException("Windows signature trust failed: " + Path.GetFileName(path)); }
        finally { trust.StateAction = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref trust); Marshal.DestroyStructure<WinTrustFile>(pointer); Marshal.FreeHGlobal(pointer); }
    }
    public static SuiteManifest Extract(string archivePath, string destination) {
        Directory.CreateDirectory(destination); NoLinks(destination);
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > 1002 || archive.Entries.Sum(e => e.Length) > 1024L * 1024 * 1024) throw new InvalidDataException("Release archive exceeds limits.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries) {
            SuiteManifest.SafeRelative(entry.FullName);
            if (!names.Add(entry.FullName) || entry.Length > 256 * 1024 * 1024 || (entry.ExternalAttributes >> 16 & 0xf000) == 0xa000) throw new InvalidDataException("Invalid archive entry.");
            string target = Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); NoLinks(Path.GetDirectoryName(target)!);
            using var source = entry.Open(); using var output = new FileStream(target, FileMode.CreateNew);
            var buffer = new byte[65536]; int count; long copied = 0;
            while ((count = source.Read(buffer)) > 0) {
                copied += count; if (copied > entry.Length) throw new InvalidDataException("Archive expansion exceeded its declared size.");
                output.Write(buffer, 0, count);
            }
            if (copied != entry.Length) throw new InvalidDataException("Truncated archive entry.");
        }
        var manifest = VerifyDirectory(destination);
        var allowed = manifest.Files.Select(f => f.Path).Append("suite-manifest.json").Append("suite-manifest.p7s").ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.SetEquals(allowed)) throw new InvalidDataException("Unlisted release files.");
        return manifest;
    }
    public static void NoLinks(string path) {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Release paths cannot traverse links.");
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WinTrustFile {
        public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr File, Subject;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WinTrustData {
        public uint Size; public IntPtr Policy, Sip; public uint UI, Revocation, Choice; public IntPtr File;
        public uint StateAction; public IntPtr State, Url; public uint Flags, Context; public IntPtr Settings;
    }
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WinTrustData data);
}
