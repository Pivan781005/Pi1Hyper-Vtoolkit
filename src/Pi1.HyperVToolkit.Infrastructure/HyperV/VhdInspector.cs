using Microsoft.Extensions.Logging;
using Pi1.HyperVToolkit.Core.Services;
using Pi1.HyperVToolkit.Core.Storage;
using Pi1.HyperVToolkit.Infrastructure.Cim;
using Pi1.HyperVToolkit.Infrastructure.Mapping;

namespace Pi1.HyperVToolkit.Infrastructure.HyperV;

// Native Get-VHD parity without PowerShell hosting: Size/Type/Format come from
// Msvm_ImageManagementService.GetVirtualHardDiskSettingData (embedded
// Msvm_VirtualHardDiskSettingData), FileSize from the file itself (local) or
// CIM_DataFile (remote). One bad disk yields null — never a batch failure.
public sealed class VhdInspector : IVhdInspector
{
    private readonly ICimQuerier _cim;
    private readonly ILogger<VhdInspector> _logger;

    public VhdInspector(ICimQuerier cim, ILogger<VhdInspector> logger)
    {
        _cim = cim ?? throw new ArgumentNullException(nameof(cim));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<VhdMetadata?> InspectAsync(
        string node, string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var outputs = await _cim.InvokeSingletonMethodAsync(
                node,
                HyperVMapper.HyperVNamespace,
                "Msvm_ImageManagementService",
                "GetVirtualHardDiskSettingData",
                new Dictionary<string, object?> { ["Path"] = path },
                TimeSpan.FromSeconds(60),
                cancellationToken).ConfigureAwait(false);

            if (!IsSuccess(outputs))
            {
                _logger.LogDebug(
                    "GetVirtualHardDiskSettingData pre {Path} na uzle {Node} vrátilo {ReturnValue}.",
                    path, node, outputs.TryGetValue("ReturnValue", out var rv) ? rv : "?");
                return null;
            }

            var setting = VhdSettingDataParser.Parse(
                outputs.TryGetValue("SettingData", out var raw)
                    ? Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture)
                    : null);
            if (setting.Type is null && setting.Format is null && setting.MaxInternalSize is null)
            {
                _logger.LogDebug("VHD {Path} na uzle {Node}: prázdne SettingData.", path, node);
                return null;
            }

            var fileSize = await ReadFileSizeAsync(node, path, cancellationToken).ConfigureAwait(false);
            return new VhdMetadata(
                string.IsNullOrEmpty(setting.Path) ? path : setting.Path,
                VhdSettingDataParser.MapVhdType(setting.Type),
                VhdSettingDataParser.MapVhdFormat(setting.Format),
                setting.MaxInternalSize ?? 0,
                fileSize);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // PowerShell silently catches Get-VHD failure per disk; C# keeps the
            // user-visible Unknown/empty semantics but logs the diagnosis.
            _logger.LogDebug(ex, "Inšpekcia VHD {Path} na uzle {Node} zlyhala.", path, node);
            return null;
        }
    }

    private static bool IsSuccess(IReadOnlyDictionary<string, object?> outputs)
    {
        if (!outputs.TryGetValue("ReturnValue", out var value) || value is null)
        {
            return false;
        }

        try
        {
            var code = Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            return code == 0;
        }
        catch (Exception) when (value is IConvertible)
        {
            return false;
        }
    }

    private async Task<ulong?> ReadFileSizeAsync(string node, string path, CancellationToken ct)
    {
        try
        {
            if (IsLocalNode(node))
            {
                var info = new FileInfo(path);
                return info.Exists ? (ulong)info.Length : null;
            }

            var escaped = path.Replace("\\", "\\\\").Replace("'", "\\'");
            var rows = await _cim.QueryAsync(
                node, @"root\cimv2",
                $"SELECT FileSize FROM CIM_DataFile WHERE Name = '{escaped}'",
                TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            return rows.Count > 0 ? CimValues.GetULong(rows[0], "FileSize") : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Veľkosť súboru {Path} na uzle {Node} sa nedá zistiť.", path, node);
            return null;
        }
    }

    private static bool IsLocalNode(string node) =>
        node is "." or "localhost" ||
        string.Equals(node, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
}
