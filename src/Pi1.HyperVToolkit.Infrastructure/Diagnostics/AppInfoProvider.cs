using System.Reflection;
using System.Runtime.InteropServices;
using Pi1.HyperVToolkit.Core.Services;

namespace Pi1.HyperVToolkit.Infrastructure.Diagnostics;

/// <summary>Host/process facts for startup logging.</summary>
public sealed class AppInfoProvider : IAppInfoProvider
{
    public string ApplicationVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

    public string MachineName => Environment.MachineName;

    public string UserName
    {
        get
        {
            try
            {
                return $"{Environment.UserDomainName}\\{Environment.UserName}";
            }
            catch (Exception)
            {
                return Environment.UserName;
            }
        }
    }

    public string OsDescription => RuntimeInformation.OSDescription;
    public string ProcessArchitecture => RuntimeInformation.ProcessArchitecture.ToString();
    public string RuntimeVersion => RuntimeInformation.FrameworkDescription;
}
