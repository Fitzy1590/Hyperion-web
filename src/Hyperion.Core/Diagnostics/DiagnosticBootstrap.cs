using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Web.WebView2.Core;

namespace Hyperion.Core.Diagnostics;

public enum CheckStatus { Healthy, Warning, Failed }

/// <param name="Fix">Repair action, or null when nothing can be automated (e.g. GPU drivers).</param>
public sealed record CheckResult(string Name, CheckStatus Status, string Detail, Func<CancellationToken, Task>? Fix = null);

/// <summary>Pre-launch environment checks (WebView2, VC++ runtime, DirectX, Widevine) plus the 1-click repair.</summary>
public sealed class DiagnosticBootstrap(HttpClient? http = null)
{
    // Assumptions, adjust if needed: oldest WebView2 runtime we accept, and oldest VC++ 14.x runtime considered current.
    public const string MinWebView2Version = "120.0.0.0";
    const int MinVcMinor = 38;

    const string WebView2BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
    static readonly string VcRedistUrl = RuntimeInformation.OSArchitecture == Architecture.Arm64
        ? "https://aka.ms/vs/17/release/vc_redist.arm64.exe"
        : "https://aka.ms/vs/17/release/vc_redist.x64.exe";

    readonly HttpClient _http = http ?? new HttpClient();

    public Task<IReadOnlyList<CheckResult>> RunAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<CheckResult>>(() => [CheckWebView2(), CheckVcRuntime(), CheckDirectX(), CheckWidevine()], ct);

    /// <summary>Runs every available fix once (WebView2 and VC++ installers are shared across checks).</summary>
    public async Task AutoFixAsync(IEnumerable<CheckResult> results, CancellationToken ct = default)
    {
        foreach (var r in results.Where(r => r.Status != CheckStatus.Healthy && r.Fix is not null).DistinctBy(r => r.Fix!.Method))
            await r.Fix!(ct);
    }

    CheckResult CheckWebView2()
    {
        const string name = "WebView2 Runtime";
        try
        {
            var v = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return CoreWebView2Environment.CompareBrowserVersions(v, MinWebView2Version) < 0
                ? new(name, CheckStatus.Failed, $"Version {v} is older than {MinWebView2Version}.", InstallWebView2Async)
                : new(name, CheckStatus.Healthy, $"Version {v}");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return new(name, CheckStatus.Failed, "Not installed.", InstallWebView2Async);
        }
    }

    CheckResult CheckVcRuntime()
    {
        const string name = "Visual C++ Redistributable (2015-2026)";
        foreach (var dll in new[] { "vcruntime140.dll", "msvcp140.dll" })
        {
            var path = Path.Combine(Environment.SystemDirectory, dll);
            if (!File.Exists(path))
                return new(name, CheckStatus.Failed, $"{dll} is missing.", InstallVcRedistAsync);
            if (!NativeLibrary.TryLoad(path, out var h))
                return new(name, CheckStatus.Failed, $"{dll} is present but cannot be loaded (corrupted?).", InstallVcRedistAsync);
            NativeLibrary.Free(h);
            var fv = FileVersionInfo.GetVersionInfo(path);
            if (fv.FileMajorPart < 14 || (fv.FileMajorPart == 14 && fv.FileMinorPart < MinVcMinor))
                return new(name, CheckStatus.Warning, $"{dll} {fv.FileVersion} is outdated.", InstallVcRedistAsync);
        }
        return new(name, CheckStatus.Healthy, "vcruntime140.dll and msvcp140.dll OK");
    }

    [DllImport("d3d11.dll")]
    static extern int D3D11CreateDevice(IntPtr adapter, uint driverType, IntPtr software, uint flags, uint[] featureLevels,
        uint count, uint sdkVersion, out IntPtr device, out uint featureLevel, out IntPtr context);

    [DllImport("d3d12.dll")]
    static extern int D3D12CreateDevice(IntPtr adapter, uint minFeatureLevel, ref Guid riid, IntPtr device);

    static CheckResult CheckDirectX()
    {
        const string name = "DirectX GPU acceleration";
        try
        {
            var hr = D3D11CreateDevice(IntPtr.Zero, 1 /* HARDWARE */, IntPtr.Zero, 0, [0xb100u, 0xb000u], 2, 7,
                out var dev, out var level, out var ctx);
            if (hr != 0)
                return new(name, CheckStatus.Failed, $"No hardware Direct3D 11 device (HRESULT 0x{hr:X8}). Update your GPU driver.");
            Marshal.Release(ctx);
            Marshal.Release(dev);

            var iid = new Guid("189819f1-1db6-4b57-be54-1821339b85f7"); // ID3D12Device
            var dx12 = false;
            try { dx12 = D3D12CreateDevice(IntPtr.Zero, 0xb000, ref iid, IntPtr.Zero) == 0; }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { /* DX12 not available */ }
            return new(name, CheckStatus.Healthy, $"Direct3D 11 (feature level 0x{level:X}){(dx12 ? ", Direct3D 12 supported" : "")}");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return new(name, CheckStatus.Failed, "Direct3D runtime not found.");
        }
    }

    // Assumption: the WebView2 runtime ships a WidevineCdm folder next to its binaries. File presence is only a hint;
    // real readiness means requestMediaKeySystemAccess('com.widevine.alpha') succeeds in a live WebView.
    CheckResult CheckWidevine()
    {
        const string name = "Widevine CDM (Kayo / Netflix / Prime Video)";
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        }.Select(p => Path.Combine(p, "Microsoft", "EdgeWebView", "Application")).Where(Directory.Exists);

        var found = roots.SelectMany(r => Directory.EnumerateDirectories(r)).Any(d => Directory.Exists(Path.Combine(d, "WidevineCdm")));
        return found
            ? new(name, CheckStatus.Healthy, "WidevineCdm folder found in the WebView2 runtime.")
            : new(name, CheckStatus.Warning, "WidevineCdm not found next to the WebView2 runtime. Updating the runtime may fix it.", InstallWebView2Async);
    }

    Task InstallWebView2Async(CancellationToken ct) =>
        RunInstallerAsync(WebView2BootstrapperUrl, "MicrosoftEdgeWebview2Setup.exe", "/silent /install", ct);

    Task InstallVcRedistAsync(CancellationToken ct) =>
        RunInstallerAsync(VcRedistUrl, Path.GetFileName(VcRedistUrl), "/install /quiet /norestart", ct);

    async Task RunInstallerAsync(string url, string fileName, string args, CancellationToken ct)
    {
        var dir = Directory.CreateTempSubdirectory("hyperion-fix-").FullName;
        var path = Path.Combine(dir, fileName);
        await using (var src = await _http.GetStreamAsync(url, ct))
        await using (var dst = File.Create(path))
            await src.CopyToAsync(dst, ct);

        VerifyMicrosoftSignature(path);

        using var p = Process.Start(new ProcessStartInfo(path, args) { UseShellExecute = true, Verb = "runas" })
                      ?? throw new InvalidOperationException("Installer did not start.");
        await p.WaitForExitAsync(ct);
        // 0 = ok, 3010 = ok/reboot required, 1638 = newer version already installed
        if (p.ExitCode is not (0 or 3010 or 1638))
            throw new InvalidOperationException($"{fileName} exited with code {p.ExitCode}.");
    }

    static void VerifyMicrosoftSignature(string path)
    {
        using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
        using var chain = new X509Chain();
        if (!chain.Build(cert) || !cert.Subject.Contains("O=Microsoft Corporation", StringComparison.Ordinal))
            throw new InvalidOperationException($"{Path.GetFileName(path)} is not signed by Microsoft; refusing to run it.");
    }
}
