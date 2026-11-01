namespace Harmonia.Suite;

public interface IDeploymentPlatform {
    void Stop();
    void InstallDriver();
    void InstallService();
    void VerifyHealth();
    void Commit();
    void Restore();
}

public sealed class DeploymentRebootRequiredException(string message) : IOException(message);

public static class DeploymentTransaction {
    public static void Run(IDeploymentPlatform platform, bool updateDriver, bool updateService) {
        try {
            platform.Stop();
            if (updateDriver) platform.InstallDriver();
            if (updateService) platform.InstallService();
            platform.VerifyHealth(); platform.Commit();
        } catch (DeploymentRebootRequiredException) { throw; }
        catch (Exception installation) {
            try { platform.Restore(); }
            catch (Exception rollback) { throw new AggregateException("Installation and rollback both failed; use the retained installation journal to recover.", installation, rollback); }
            throw new IOException("Suite installation failed; the previous version was restored.", installation);
        }
    }
}
