using System.Runtime.InteropServices;
using System.Security.Principal;
using Pi1.HyperVToolkit.Core.Services;

namespace Pi1.HyperVToolkit.Infrastructure.Security;

/// <summary>Elevation detection via WindowsIdentity. Windows-only by design.</summary>
public sealed class PrivilegeService : IPrivilegeService
{
    public PrivilegeService()
    {
        IsAdministrator = DetectAdministrator();
    }

    public bool IsAdministrator { get; }

    public string ElevationLabel => IsAdministrator ? "Administrátor: Áno" : "Administrátor: Nie";

    private static bool DetectAdministrator()
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            // Detection must never crash startup; worst case we report non-elevated.
            return false;
        }
    }
}
