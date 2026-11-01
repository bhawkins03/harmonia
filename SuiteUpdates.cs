using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Harmonia.Suite;

namespace Harmonia;

internal static class SuiteUpdates {
    public static Version AppVersion { get { var version = typeof(MainForm).Assembly.GetName().Version!; return new(version.Major, version.Minor, version.Build); } }
    private static readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(90) };
    public sealed record Release(Version Version, Uri Archive, string Title);
    public static async Task<Release?> Find() {
        ReleaseSecurity.RequirePublisher();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/bhawkins03/harmonia/releases?per_page=100");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Harmonia-Suite", AppVersion.ToString()));
        using var response = await http.SendAsync(request); response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Release index too large.");
        using var json = JsonDocument.Parse(bytes);
        Release? latest = null;
        foreach (var release in json.RootElement.EnumerateArray()) {
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean() ||
                !tag.EndsWith("-suite", StringComparison.Ordinal) || !tag.StartsWith('v') ||
                !Version.TryParse(tag[1..^6], out var version) || version <= AppVersion || (latest is not null && version <= latest.Version)) continue;
            foreach (var asset in release.GetProperty("assets").EnumerateArray()) {
                if (asset.GetProperty("name").GetString() != "Harmonia-Suite-win-x64.zip") continue;
                var uri = new Uri(asset.GetProperty("browser_download_url").GetString()!);
                if (uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith("/bhawkins03/harmonia/releases/download/", StringComparison.Ordinal)) throw new InvalidDataException("Unexpected release URL.");
                latest = new(version, uri, tag);
            }
        }
        return latest;
    }
    public static async Task<string> Stage(Release release) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Harmonia", "SuiteUpdates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string archive = Path.Combine(directory, "release.zip"), destination = Path.Combine(directory, "verified");
        Uri uri = release.Archive;
        for (int redirects = 0; ; redirects++) {
            if (uri.Scheme != "https" || !(uri.Host == "github.com" || uri.Host == "release-assets.githubusercontent.com" || uri.Host.EndsWith(".githubusercontent.com", StringComparison.Ordinal))) throw new IOException("Untrusted download host.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri); request.Headers.UserAgent.ParseAdd("Harmonia-Suite/0.3.0");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if ((int)response.StatusCode is >= 300 and < 400) {
                if (redirects >= 5 || response.Headers.Location is null) throw new IOException("Invalid release redirect.");
                uri = new Uri(uri, response.Headers.Location); continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 1024L * 1024 * 1024) throw new IOException("Download too large.");
            using var input = await response.Content.ReadAsStreamAsync(timeout.Token); using var output = File.Create(archive);
            var buffer = new byte[65536]; long total = 0; int count;
            while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0) { total += count; if (total > 1024L * 1024 * 1024) throw new IOException("Download too large."); await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token); }
            break;
        }
        var manifest = await Task.Run(() => ReleaseSecurity.Extract(archive, destination));
        if (Version.Parse(manifest.Version) != release.Version) throw new IOException("Release tag and signed manifest versions differ.");
        return destination;
    }
    public static void Install(string stage) {
        // Use the installed, protected bootstrap, never an executable from the
        // user-writable download directory for privileged installation.
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Harmonia Suite");
        ReleaseSecurity.NoLinks(root);
        using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "installed.json")));
        string directory = state.RootElement.GetProperty("InstallerDirectory").GetString() ?? throw new IOException("Installed bootstrap path missing.");
        if (!Path.GetFullPath(directory).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Bootstrap path leaves the protected installation.");
        string installer = Path.Combine(directory, "Harmonia.Suite.Installer.exe");
        ReleaseSecurity.NoLinks(installer); ReleaseSecurity.VerifyExecutable(installer);
        var start = new ProcessStartInfo(installer) { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add("--install"); start.ArgumentList.Add(stage);
        start.ArgumentList.Add("--wait-pid"); start.ArgumentList.Add(Environment.ProcessId.ToString());
        _ = Process.Start(start) ?? throw new IOException("Unable to start installer.");
    }
}
