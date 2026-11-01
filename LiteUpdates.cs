using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Harmonia;

internal static class LiteUpdates
{
    private const string Api = "https://api.github.com/repos/bhawkins03/harmonia/releases?per_page=20";
    private const string Prefix = "v";
    private const string Suffix = "-lite-preview";
    private const string AssetName = "Harmonia-Lite-win-x64.zip";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    static LiteUpdates()
    {
        Http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Harmonia-Lite", "0.1"));
        Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    private sealed record Available(Version Version, string Tag, string Url, long Size);

    private static Version CurrentVersion => typeof(LiteUpdates).Assembly.GetName().Version ?? new Version(0, 0, 0);

    private static async Task<Available?> FindNewerAsync(CancellationToken ct)
    {
        using var response = await Http.GetAsync(Api, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        Available? best = null;
        foreach (var release in json.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith(Prefix, StringComparison.Ordinal) ||
                !tag.EndsWith(Suffix, StringComparison.Ordinal)) continue;
            if (!Version.TryParse(tag[1..^Suffix.Length], out var version) || version <= CurrentVersion) continue;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != AssetName) continue;
                var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                    uri.Host != "github.com" || !uri.AbsolutePath.StartsWith("/bhawkins03/harmonia/releases/download/", StringComparison.Ordinal))
                    continue;
                var size = asset.GetProperty("size").GetInt64();
                if (size < 1 || size > 300_000_000) continue;
                if (best is null || version > best.Version) best = new Available(version, tag, url, size);
            }
        }
        return best;
    }

    internal static async Task CheckAsync(Form owner, bool manual)
    {
        Available? update;
        try { update = await FindNewerAsync(CancellationToken.None); }
        catch (Exception ex)
        {
            if (manual && !owner.IsDisposed)
                MessageBox.Show(owner, "Could not check GitHub Releases. " + ex.Message, "Harmonia Lite", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (owner.IsDisposed) return;
        if (update is null)
        {
            if (manual) MessageBox.Show(owner, "You're using the latest available Lite version.", "Harmonia Lite");
            return;
        }
        if (MessageBox.Show(owner,
            $"Harmonia Lite {update.Version} is available (installed: {CurrentVersion}).\n\nDownload and install the update now? Your speaker settings will be preserved.",
            "Harmonia Lite update", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        string? folder = null;
        try
        {
            folder = Path.Combine(Path.GetTempPath(), "Harmonia-Lite-Update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string archive = Path.Combine(folder, AssetName);
            using (var response = await Http.GetAsync(update.Url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync();
                await using var output = File.Create(archive);
                await input.CopyToAsync(output);
            }
            if (new FileInfo(archive).Length != update.Size) throw new InvalidDataException("Download size does not match GitHub's release metadata.");
            string appFile = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");
            string appDir = Path.GetDirectoryName(appFile) ?? throw new InvalidOperationException("Application directory unavailable.");
            string candidate = Path.Combine(folder, "candidate");
            Directory.CreateDirectory(candidate);
            ZipFile.ExtractToDirectory(archive, candidate);
            if (!File.Exists(Path.Combine(candidate, "Harmonia.exe"))) throw new InvalidDataException("Release does not include Harmonia.exe.");
            // Refuse unknown paths: this updater only supports a portable single EXE plus small sidecars.
            foreach (var path in Directory.GetFiles(candidate, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(candidate, path);
                if (relative != "Harmonia.exe" && relative != "README-FIRST.txt")
                    throw new InvalidDataException("Unexpected content in release archive: " + relative);
            }
            string helper = Path.Combine(folder, "Harmonia-Updater.exe");
            File.Copy(appFile, helper);
            var args = new[] { "--lite-apply-update", Environment.ProcessId.ToString(), candidate, appDir };
            Process.Start(new ProcessStartInfo(helper) { UseShellExecute = false, ArgumentList = { args[0], args[1], args[2], args[3] } })
                ?? throw new InvalidOperationException("Could not start update helper.");
            // Quit via the same shutdown path used by the tray Exit command.
            owner.Tag = "lite-update-close";
            owner.Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Update failed. Your current installation has not been changed.\n\n" + ex.Message,
                "Harmonia Lite", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // Invoked from a temporary copy of the executable after the main app exits.
    internal static int Apply(string[] args)
    {
        if (args.Length != 4 || !int.TryParse(args[1], out int pid)) return 1;
        try
        {
            string candidate = Path.GetFullPath(args[2]);
            string destination = Path.GetFullPath(args[3]);
            if (!Path.GetFileName(candidate).Equals("candidate", StringComparison.Ordinal) ||
                !Directory.Exists(candidate) || !Directory.Exists(destination) ||
                !File.Exists(Path.Combine(candidate, "Harmonia.exe"))) return 1;
            try { using var previous = Process.GetProcessById(pid); previous.WaitForExit(30000); if (!previous.HasExited) return 1; }
            catch (ArgumentException) { }
            File.Copy(Path.Combine(candidate, "Harmonia.exe"), Path.Combine(destination, "Harmonia.exe"), overwrite: true);
            string readme = Path.Combine(candidate, "README-FIRST.txt");
            if (File.Exists(readme)) File.Copy(readme, Path.Combine(destination, "README-FIRST.txt"), overwrite: true);
            Process.Start(new ProcessStartInfo(Path.Combine(destination, "Harmonia.exe")) { WorkingDirectory = destination, UseShellExecute = true });
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not apply the Lite update. You may need write access to the application folder.\n\n" + ex.Message,
                "Harmonia Lite", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
