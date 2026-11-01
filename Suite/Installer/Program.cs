using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Win32;

namespace Harmonia.Suite.Installer;

internal sealed record InstalledState(string Version, string AppDirectory, string ServiceDirectory, string DriverDirectory, string ServiceVersion, string DriverVersion, string InstallerDirectory);
internal sealed record Journal(InstalledState? Previous, InstalledState Next, bool DriverChanged, bool ServiceChanged, string Phase = "Prepared");
internal static class Program {
    public static int Main(string[] args) {
        try {
            if (args.Length == 2 && args[0] == "--verify") {
                var verified = ReleaseSecurity.VerifyDirectory(args[1]); Console.WriteLine("Verified Suite package " + verified.Version); return 0;
            }
            if (args.Length == 0) args = ["--install", Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."))];
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) {
                var elevated = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
                foreach (string argument in args) elevated.ArgumentList.Add(argument);
                _ = Process.Start(elevated) ?? throw new IOException("Installer elevation was cancelled."); return 0;
            }
            using var mutex = new Mutex(false, @"Global\Harmonia.Suite.Installation");
            if (!mutex.WaitOne(0)) throw new IOException("Another Suite installation is running.");
            try {
                var installer = new Installer();
                if (args[0] == "--recover") installer.Recover();
                else if (args[0] == "--resume") installer.Resume();
                else if (args[0] == "--uninstall") installer.Uninstall();
                else if (args[0] == "--install" && args.Length >= 2) {
                    int wait = Array.IndexOf(args, "--wait-pid");
                    if (wait >= 0 && wait + 1 < args.Length && int.TryParse(args[wait + 1], out int pid)) {
                        try { if (!Process.GetProcessById(pid).WaitForExit(30000)) throw new IOException("Close Harmonia Suite before updating."); } catch (ArgumentException) { }
                    }
                    installer.Install(args[1]);
                } else throw new ArgumentException("Invalid installer arguments.");
            } finally { mutex.ReleaseMutex(); }
            return 0;
        } catch (DeploymentRebootRequiredException ex) { Console.Error.WriteLine(ex.Message); MessageBox.Show(ex.Message, "Suite reboot required"); return 3010; }
        catch (Exception ex) { Console.Error.WriteLine(ex); if (args.Length == 0 || args[0] != "--verify") MessageBox.Show(ex.Message, "Suite installation failed"); return 1; }
    }
}

internal sealed class Installer : IDeploymentPlatform {
    private const string ServiceName = "HarmoniaAudioTransport";
    private const string ServiceSid = "S-1-5-80-3033135252-3531722472-1049171237-837529978-1616513259";
    private readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Harmonia Suite");
    private string StatePath => Path.Combine(root, "installed.json");
    private string JournalPath => Path.Combine(root, "pending.json");
    private Journal? journal;
    public void Install(string source) {
        if (File.Exists(JournalPath)) throw new IOException("An interrupted installation needs --recover first.");
        var manifest = ReleaseSecurity.VerifyDirectory(source);
        SecureRoot();
        var previous = ReadState();
        using (var existing = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + ServiceName)) {
            if (existing is not null) {
                string expected = previous is null ? "" : "\"" + Path.Combine(previous.ServiceDirectory, "HarmoniaAudioTransport.exe") + "\"";
                if (previous is null || !string.Equals(existing.GetValue("ImagePath") as string, expected, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("An unmanaged transport service exists. Review/uninstall it before installing this Suite package.");
            }
        }
        if (previous is not null && Version.Parse(manifest.Version) <= Version.Parse(previous.Version)) throw new IOException("Update version must be newer; use --recover for interrupted installations.");
        if (previous is null && (!manifest.UpdateDriver || !manifest.UpdateService)) throw new IOException("Initial installation requires all Suite components.");
        string destination = Path.Combine(root, "versions", manifest.Version);
        if (Directory.Exists(destination)) throw new IOException("Version directory already exists. Review retained installation before retrying.");
        Directory.CreateDirectory(destination); ReleaseSecurity.NoLinks(destination);
        foreach (var file in manifest.Files) {
            string target = Path.Combine(destination, file.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(source, file.Path.Replace('/', Path.DirectorySeparatorChar)), target);
        }
        foreach (string name in new[] { "suite-manifest.json", "suite-manifest.p7s" }) File.Copy(Path.Combine(source, name), Path.Combine(destination, name));
        // Reverify the protected copy: no user-writable staging path is executed.
        ReleaseSecurity.VerifyDirectory(destination);
        string serviceVersion = manifest.UpdateService ? manifest.ServiceVersion : previous!.ServiceVersion;
        string driverVersion = manifest.UpdateDriver ? manifest.DriverVersion : previous!.DriverVersion;
        manifest.Compatible(serviceVersion, driverVersion);
        var next = new InstalledState(manifest.Version, Path.Combine(destination, "App"),
            manifest.UpdateService ? Path.Combine(destination, "Service") : previous!.ServiceDirectory,
            manifest.UpdateDriver ? Path.Combine(destination, "Driver") : previous!.DriverDirectory, serviceVersion, driverVersion, Path.Combine(destination, "Installer"));
        journal = new(previous, next, manifest.UpdateDriver, manifest.UpdateService);
        AtomicWrite(JournalPath, journal);
        DeploymentTransaction.Run(this, manifest.UpdateDriver, manifest.UpdateService);
        Console.WriteLine("Suite installed. Previous versions and user configuration were retained.");
        LaunchUnelevated(next.AppDirectory);
    }
    private void SecureRoot() {
        Directory.CreateDirectory(root); ReleaseSecurity.NoLinks(root);
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        foreach (string sid in new[] { "S-1-5-18", "S-1-5-32-544" }) acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        foreach (string sid in new[] { "S-1-5-32-545", ServiceSid }) acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(root).SetAccessControl(acl);
    }
    public void Stop() {
        using var service = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == ServiceName);
        if (service is not null && service.Status != ServiceControllerStatus.Stopped) { service.Stop(); service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30)); }
    }
    public void InstallDriver() {
        if (DriverSetup.Install(Path.Combine(journal!.Next.DriverDirectory, "HarmoniaVirtualAudio.inf"))) {
            journal = journal with { Phase = "AwaitingReboot" }; AtomicWrite(JournalPath, journal);
            throw new DeploymentRebootRequiredException("The verified driver is staged and Windows requires a reboot. Reboot, then run the protected Suite installer with --resume. The prior version and recovery journal are retained; --recover restores it instead.");
        }
    }
    public void InstallService() => ConfigureService(journal!.Next.ServiceDirectory);
    private static void ConfigureService(string directory) {
        string binary = Path.Combine(directory, "HarmoniaAudioTransport.exe");
        bool exists = ServiceController.GetServices().Any(s => s.ServiceName == ServiceName);
        Run("sc.exe", exists ? "config" : "create", ServiceName, "binPath=", "\"" + binary + "\"", "type=", "own", "start=", "auto", "obj=", @"NT AUTHORITY\LocalService", "password=", "");
        Run("sc.exe", "sidtype", ServiceName, "restricted");
        Run("sc.exe", "failure", ServiceName, "reset=", "86400", "actions=", "restart/5000/restart/15000/restart/30000");
        // Only SYSTEM and administrators can alter/start/stop the service.
        Run("sc.exe", "sdset", ServiceName, "D:P(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)");
    }
    public void VerifyHealth() {
        using var service = new ServiceController(ServiceName);
        if (service.Status != ServiceControllerStatus.Running) service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
        var reply = QueryStatus();
        var expected = journal!.Next;
        if (!reply.Success || Version.Parse(reply.Status.ServiceVersion) != Version.Parse(expected.ServiceVersion) ||
            Version.Parse(reply.Status.DriverVersion) != Version.Parse(expected.DriverVersion)) throw new IOException("Installed service/driver health or version check failed.");
        // Driver presence and code-integrity validation are checked by Windows
        // during binding; version health alone never overrides signature checks.
    }
    private static ControlReply QueryStatus() {
        using var token = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var pipe = new NamedPipeClientStream(".", ControlWire.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
        pipe.ConnectAsync(token.Token).GetAwaiter().GetResult();
        ControlWire.WriteAsync(pipe, new ControlRequest("health"), token.Token).GetAwaiter().GetResult();
        return ControlWire.ReadAsync<ControlReply>(pipe, token.Token).GetAwaiter().GetResult();
    }
    public void Commit() {
        AtomicWrite(StatePath, journal!.Next);
        string installer = journal.Next.InstallerDirectory;
        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\HarmoniaSuite");
        key.SetValue("DisplayName", "Harmonia Suite"); key.SetValue("DisplayVersion", journal.Next.Version);
        key.SetValue("UninstallString", "\"" + Path.Combine(installer, "Harmonia.Suite.Installer.exe") + "\" --uninstall");
        File.Delete(JournalPath);
    }
    public void Restore() {
        Stop();
        var previous = journal!.Previous;
        if (previous is null) {
            if (journal.DriverChanged) DriverSetup.Remove();
            if (ServiceController.GetServices().Any(s => s.ServiceName == ServiceName)) Run("sc.exe", "delete", ServiceName);
            if (File.Exists(StatePath)) File.Delete(StatePath);
        } else {
            if (journal.DriverChanged && DriverSetup.Install(Path.Combine(previous.DriverDirectory, "HarmoniaVirtualAudio.inf"))) throw new IOException("Rollback driver requires reboot. Journal retained for --recover after restart.");
            ConfigureService(previous.ServiceDirectory);
            using var service = new ServiceController(ServiceName); service.Start(); service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            var health = QueryStatus();
            if (health.Status.ServiceVersion != previous.ServiceVersion || Version.Parse(health.Status.DriverVersion) != Version.Parse(previous.DriverVersion)) throw new IOException("Rollback health check failed.");
            AtomicWrite(StatePath, previous);
        }
        File.Delete(JournalPath);
    }
    public void Recover() {
        SecureRoot(); journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(JournalPath)) ?? throw new IOException("No recovery journal.");
        Restore(); Console.WriteLine("Previous Suite installation restored; staged versions retained.");
    }
    public void Resume() {
        SecureRoot(); journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(JournalPath)) ?? throw new IOException("No pending installation.");
        if (journal.Phase != "AwaitingReboot") throw new IOException("This installation needs --recover rather than resume.");
        ReleaseSecurity.VerifyDirectory(Path.Combine(root, "versions", journal.Next.Version));
        DeploymentTransaction.Run(this, false, journal.ServiceChanged);
        LaunchUnelevated(journal.Next.AppDirectory);
    }
    public void Uninstall() {
        SecureRoot(); if (File.Exists(JournalPath)) throw new IOException("Recover the pending installation before uninstalling.");
        if (ReadState() is null) throw new IOException("No managed Suite installation found.");
        Stop(); Run("sc.exe", "delete", ServiceName); DriverSetup.Remove();
        File.Move(StatePath, StatePath + ".uninstalled-" + DateTime.UtcNow.Ticks);
        Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\HarmoniaSuite", false);
        Console.WriteLine("Service and exact Harmonia devnode removed. Driver store, previous versions and user settings retained for recovery. Windows may require a reboot.");
    }
    private InstalledState? ReadState() => File.Exists(StatePath) ? JsonSerializer.Deserialize<InstalledState>(File.ReadAllText(StatePath)) : null;
    private static void AtomicWrite<T>(string path, T value) { File.WriteAllText(path + ".new", JsonSerializer.Serialize(value)); File.Move(path + ".new", path, true); }
    private static void Run(string program, params string[] args) {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, program)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in args) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start " + program);
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000)) throw new IOException(program + " timed out; review the installation journal.");
        if (process.ExitCode != 0) throw new IOException(program + ": " + output.Result + error.Result);
    }
    private static void LaunchUnelevated(string directory) {
        // Explorer brokers launch into the existing desktop shell. UI startup
        // separately refuses an elevated token rather than staying privileged.
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true };
        start.ArgumentList.Add(Path.Combine(directory, "Harmonia.exe")); Process.Start(start);
    }
}
