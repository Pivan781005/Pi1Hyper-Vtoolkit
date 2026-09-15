using System.Diagnostics;
using System.Text;

namespace Pi1.HyperVToolkit.Tests.Parity;

/// <summary>
/// Developer-only parity bridge: invokes the reference PowerShell collectors
/// (the untouched Modules\*.psm1) on the local machine in Local scope and
/// returns their JSON output. Never ships in the application; used only to
/// compare C# collectors against the canonical implementation.
/// </summary>
public static class PowerShellReference
{
    public sealed record ReferenceResult(bool IsAvailable, string Reason, string Json, string Preamble);

    public static string? FindModulesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var level = 0; level < 8 && directory is not null; level++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Modules", "Pi1.Data.psm1");
            if (File.Exists(candidate))
            {
                return Path.Combine(directory.FullName, "Modules");
            }
        }

        return null;
    }

    public static async Task<ReferenceResult> CollectAsync(string functionCall, TimeSpan timeout) =>
        await CollectSnippetAsync(
            functionCall,
            ["Pi1.Core.psm1", "Pi1.Data.psm1"],
            timeout).ConfigureAwait(false);

    public static async Task<ReferenceResult> CollectSnippetAsync(
        string snippet, IEnumerable<string> modules, TimeSpan timeout)
    {
        var modulesDir = FindModulesDirectory();
        if (modulesDir is null)
        {
            return new ReferenceResult(false, "NOT TESTABLE IN CURRENT ENVIRONMENT: Modules\\ not found from test directory.", string.Empty, string.Empty);
        }

        var imports = string.Join("; ", modules.Select(m => $"Import-Module '{Path.Combine(modulesDir, m)}' -Force"));
        // Force UTF-8 on the redirected stdout: Windows PowerShell 5.1 would
        // otherwise emit the system ANSI code page while this side decodes
        // UTF-8, mangling diacritics (observed: á/é/í -> ?). ASCII payloads
        // (all previously passing tests) are byte-identical either way.
        var script = $"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; {imports}; {snippet} | ConvertTo-Json -Depth 4 -Compress";

        // Transport: the COMPLETE script goes through -EncodedCommand (Base64
        // UTF-16LE) via ArgumentList. A quoted -Command "& { ... }" transport
        // is fragile: multiline functions and quoted strings inside the
        // snippet break command-line parsing (observed: "The string is
        // missing the terminator"). The script is encoded exactly once, with
        // no quote escaping beforehand.
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-EncodedCommand");
        process.StartInfo.ArgumentList.Add(encoded);

        string stdout;
        string stderr;
        try
        {
            if (!process.Start())
            {
                return new ReferenceResult(false, "NOT TESTABLE IN CURRENT ENVIRONMENT: powershell.exe could not start.", string.Empty, string.Empty);
            }
        }
        catch (Exception ex)
        {
            return new ReferenceResult(false, $"NOT TESTABLE IN CURRENT ENVIRONMENT: powershell.exe unavailable ({ex.Message}).", string.Empty, string.Empty);
        }

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            stdout = await process.StandardOutput.ReadToEndAsync(cts.Token).ConfigureAwait(false);
            stderr = await process.StandardError.ReadToEndAsync(cts.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // Best effort cleanup of a hung reference run.
            }

            return new ReferenceResult(false, "NOT TESTABLE IN CURRENT ENVIRONMENT: PowerShell reference run timed out.", string.Empty, string.Empty);
        }

        if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
        {
            var error = stderr.Trim();
            if (IsParserFailure(error))
            {
                // OUR generated script is malformed: a TEST HARNESS BUG, never
                // an environmental NOT TESTABLE condition. Fail loudly so a
                // broken snippet can never false-green a parity test.
                throw new InvalidOperationException(
                    $"PowerShell reference script failed to parse: {error}");
            }

            return new ReferenceResult(false, $"NOT TESTABLE IN CURRENT ENVIRONMENT: reference collector failed: {error}", string.Empty, string.Empty);
        }

        // The reference modules may emit WARNING lines before the JSON payload
        // (e.g. import or CIM warnings). The JSON document starts at the first
        // '{' or '['; anything before it is diagnostic preamble.
        var text = stdout.Trim();
        var jsonStart = text.IndexOfAny(['{', '[']);
        if (jsonStart < 0)
        {
            return new ReferenceResult(true, string.Empty, string.Empty, text);
        }

        return new ReferenceResult(true, string.Empty, text[jsonStart..].Trim(), text[..jsonStart].Trim());
    }

    /// <summary>
    /// Detects PowerShell language/parse failures of the GENERATED script
    /// (as opposed to runtime cmdlet errors, which stay NOT TESTABLE).
    /// </summary>
    internal static bool IsParserFailure(string stderr) =>
        !string.IsNullOrEmpty(stderr) &&
        (stderr.Contains("ParserError", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("missing the terminator", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("TerminatorExpectedAtEndOfString", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("Unexpected token", StringComparison.OrdinalIgnoreCase));
}
