using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using HeavyPro.Core.Physics;

namespace HeavyPro.App.Services;

/// <summary>
/// Start-up tamper check. Dormant until HeavyPro is Authenticode-signed; after that, it refuses to
/// run if any shipped HeavyPro assembly was modified or is not signed by Rotaidem. See
/// <see cref="IntegrityPolicy"/> for what this can and cannot do (it is not unbreakable).
/// </summary>
internal static class AssemblyIntegrity
{
    // The signer's certificate subject must contain this once a certificate is in use. An OV/EV
    // Authenticode certificate issued to Rotaidem carries the organisation name in its subject.
    private const string ExpectedPublisher = "Rotaidem";

    // The proprietary assemblies that carry the licence gate and physics. Third-party DLLs
    // (System.*, SimConnect) are signed by their own vendors, so they are not checked here.
    private static readonly string[] Guarded =
    {
        "HeavyPro.exe", "HeavyPro.Core.dll", "HeavyPro.SimConnect.dll", "HeavyPro.Logging.dll", "Rotaidem.Licensing.dll",
    };

    /// <summary>The verdict for this installation. <paramref name="reason"/> explains a refusal (or the dormant state).</summary>
    public static IntegrityVerdict Check(out string reason)
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
        var exe = Path.Combine(dir, "HeavyPro.exe");

        // "Signing active" = the running program itself is signed. If it is not, there is no
        // certificate in use yet and the whole check is dormant (matches Sign-File.ps1's behaviour).
        var signingActive = File.Exists(exe) && IsTrusted(exe);

        var allTrusted = true;
        var publisherOk = true;
        if (signingActive)
        {
            foreach (var name in Guarded)
            {
                var path = Path.Combine(dir, name);
                if (!File.Exists(path))
                    continue; // a file that is not part of this install cannot have been tampered with
                if (!IsTrusted(path)) { allTrusted = false; break; }
                if (!SignedBy(path, ExpectedPublisher)) { publisherOk = false; break; }
            }
        }

        return IntegrityPolicy.Decide(signingActive, allTrusted, publisherOk, out reason);
    }

    /// <summary>True if the file has a valid Authenticode signature that chains to a trusted root (WinVerifyTrust).</summary>
    private static bool IsTrusted(string path)
    {
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = path,
        };
        var pFile = Marshal.AllocHGlobal((int)fileInfo.cbStruct);
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = pFile,
                dwStateAction = WTD_STATEACTION_IGNORE,
                dwProvFlags = WTD_SAFER_FLAG,
            };
            var action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
            // 0 = the signature is present, intact and trusted. Anything else (unsigned, modified,
            // untrusted root) is a failure.
            return WinVerifyTrust(IntPtr.Zero, ref action, ref data) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(pFile);
        }
    }

    /// <summary>True if the file's Authenticode signer certificate subject names the expected publisher.</summary>
    private static bool SignedBy(string path, string publisher)
    {
        try
        {
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            return cert.Subject.IndexOf(publisher, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch (Exception)
        {
            // No embedded certificate, or it could not be read: treat as not matching.
            return false;
        }
    }

    // ---- WinVerifyTrust interop (wintrust.dll) ----
    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_IGNORE = 0;
    private const uint WTD_SAFER_FLAG = 0x100;
    private static Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
    }
}
