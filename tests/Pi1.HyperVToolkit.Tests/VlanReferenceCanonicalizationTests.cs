using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Pi1.HyperVToolkit.Tests.Parity;

namespace Pi1.HyperVToolkit.Tests;

// Regression tests for the VLAN parity reference canonicalization
// (NetworkParityTests.VlanIdentityCanonicalizer). They execute the EXACT same
// PowerShell function against fabricated cmdlet-output shapes — no Hyper-V,
// no elevation — proving: direct properties win, ParentAdapter is the
// fallback, missing parents yield empty, and the live DOS shape canonicalizes
// to DOS 6.22 | Network Adapter | Untagged.
public sealed class VlanReferenceCanonicalizationTests
{
    private static readonly TimeSpan RunnerTimeout = TimeSpan.FromSeconds(60);

    private static async Task<List<Dictionary<string, string>>> RunCasesAsync()
    {
        var script = NetworkParityTests.VlanIdentityCanonicalizer + """
            $cases = @()
            # 1. Direct properties present AND parent present: direct wins.
            $cases += [PSCustomObject]@{
                VMName = 'DIRECT-VM'; VMNetworkAdapterName = 'Direct NIC'
                ParentAdapter = [PSCustomObject]@{ VMName = 'PARENT-VM'; Name = 'Parent NIC' }
            }
            # 2. Blank direct + parent: fallback.
            $cases += [PSCustomObject]@{
                VMName = ''; VMNetworkAdapterName = ''
                ParentAdapter = [PSCustomObject]@{ VMName = 'FALLBACK-VM'; Name = 'Fallback NIC' }
            }
            # 3. Blank direct, no parent: empty.
            $cases += [PSCustomObject]@{
                VMName = ''; VMNetworkAdapterName = ''
            }
            # 4. Live DOS shape: no direct properties at all, ParentAdapter only.
            $dos = [PSCustomObject]@{
                OperationMode = 'Untagged'; AccessVlanId = 0; NativeVlanId = 0; AllowedVlanIdList = @()
                ParentAdapter = [PSCustomObject]@{ VMName = 'DOS 6.22'; Name = 'Network Adapter' }
            }
            $cases += $dos
            $out = @()
            foreach ($c in $cases) {
                $id = Get-PiVlanReferenceIdentity $c
                $out += [PSCustomObject]@{
                    VMName = $id.VMName; VMNetworkAdapterName = $id.VMNetworkAdapterName
                    OperationMode = [string]$c.OperationMode; AccessVlanId = $c.AccessVlanId
                    NativeVlanId = $c.NativeVlanId; AllowedVlanIdList = $c.AllowedVlanIdList
                }
            }
            $out | ConvertTo-Json -Depth 4 -Compress
            """;

        // -EncodedCommand (Base64 UTF-16LE): avoids every quoting/variable-
        // expansion pitfall of passing a script through -Command.
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        process.Start();
        using var cts = new CancellationTokenSource(RunnerTimeout);
        var stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = await process.StandardError.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);
        Assert.True(process.ExitCode == 0, $"Canonicalizer runner failed: {stderr.Trim()}");
        var text = stdout.Trim();
        var start = text.IndexOfAny(['{', '[']);
        Assert.True(start >= 0, $"No JSON payload: {text}");
        return ParityRow.ParseRows(text[start..].Trim(), ["AccessVlanId", "NativeVlanId"]);
    }

    private static List<Dictionary<string, string>>? _cached;
    private static readonly SemaphoreSlim CacheGate = new(1);

    private static async Task<List<Dictionary<string, string>>> CasesAsync()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        await CacheGate.WaitAsync();
        try
        {
            _cached ??= await RunCasesAsync();
            return _cached;
        }
        finally
        {
            CacheGate.Release();
        }
    }

    [Fact]
    public async Task DirectProperties_WinOverParent()
    {
        var row = (await CasesAsync())[0];
        Assert.Equal("DIRECT-VM", row["VMName"]);
        Assert.Equal("Direct NIC", row["VMNetworkAdapterName"]);
    }

    [Fact]
    public async Task ParentAdapter_UsedAsFallback()
    {
        var row = (await CasesAsync())[1];
        Assert.Equal("FALLBACK-VM", row["VMName"]);
        Assert.Equal("Fallback NIC", row["VMNetworkAdapterName"]);
    }

    [Fact]
    public async Task NoParent_YieldsEmpty()
    {
        var row = (await CasesAsync())[2];
        Assert.Equal(string.Empty, row["VMName"]);
        Assert.Equal(string.Empty, row["VMNetworkAdapterName"]);
    }

    [Fact]
    public async Task DosLiveShape_Canonicalizes()
    {
        var row = (await CasesAsync())[3];
        Assert.Equal("DOS 6.22", row["VMName"]);
        Assert.Equal("Network Adapter", row["VMNetworkAdapterName"]);
        Assert.Equal("Untagged", row["OperationMode"]);
        Assert.Equal("0.0", row["AccessVlanId"]);
        Assert.Equal("0.0", row["NativeVlanId"]);
        Assert.Equal(string.Empty, row["AllowedVlanIdList"]);
    }
}
