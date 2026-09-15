namespace Pi1.HyperVToolkit.Core.Services;

/// <summary>
/// Host/process facts for startup logging. Parity with the PowerShell
/// startup log lines (Root/Modules/Host/User/PSVersion).
/// </summary>
public interface IAppInfoProvider
{
    string ApplicationVersion { get; }
    string MachineName { get; }
    string UserName { get; }
    string OsDescription { get; }
    string ProcessArchitecture { get; }
    string RuntimeVersion { get; }
}
