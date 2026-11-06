namespace HeavyPro.Core.Physics;

/// <summary>What the start-up tamper check should do, given what it found about the binaries.</summary>
public enum IntegrityVerdict
{
    /// <summary>Allow start-up. Either the build is unsigned (the check is dormant) or every file verified.</summary>
    Allow,
    /// <summary>A shipped file was modified, unsigned, or signed by someone other than Rotaidem: refuse.</summary>
    Tampered,
}

/// <summary>
/// The decision half of the anti-tamper check, with no OS calls so it can be tested. The App does the
/// Authenticode verification (WinVerifyTrust) and the publisher read, then calls <see cref="Decide"/>.
///
/// Reality check: this cannot make HeavyPro unpiratable. Code that runs on the pirate's own PC can
/// always be patched. What it does do, ONCE the binaries are Authenticode-signed (see Sign-File.ps1):
/// patching any HeavyPro DLL breaks its signature, so this check fails and the stick shaping stays
/// locked. A pirate then has to strip the check from a signed exe and re-sign the whole set with a
/// certificate that shows "Rotaidem" and chains to a trusted root — which they cannot get. That turns
/// a two-minute edit into real work; it does not make it impossible. Until a certificate exists the
/// build is unsigned and this stays dormant (Allow), exactly like the signing step itself.
/// </summary>
public static class IntegrityPolicy
{
    /// <param name="signingActive">The running HeavyPro.exe itself carries an Authenticode signature (a certificate is in use).</param>
    /// <param name="allFilesTrusted">Every shipped HeavyPro assembly has a valid, trusted Authenticode signature.</param>
    /// <param name="publisherMatches">Those signatures are Rotaidem's, not a pirate's own certificate.</param>
    public static IntegrityVerdict Decide(bool signingActive, bool allFilesTrusted, bool publisherMatches, out string reason)
    {
        if (!signingActive)
        {
            // No certificate yet: the installer is unsigned, so there is nothing to verify. Do not
            // block the product (that would lock every honest customer out of an unsigned release).
            reason = "This build is not code-signed yet, so the tamper check is inactive.";
            return IntegrityVerdict.Allow;
        }
        if (allFilesTrusted && publisherMatches)
        {
            reason = "";
            return IntegrityVerdict.Allow;
        }
        reason = "A HeavyPro program file has been modified or is not signed by Rotaidem. "
            + "Reinstall HeavyPro from rotaidem.com.";
        return IntegrityVerdict.Tampered;
    }
}
