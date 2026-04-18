using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using WinBit.Core.Persistence;
using WinBit.Core.Settings;

namespace WinBit.Core.WebUi;

/// <summary>
/// Resolves an <see cref="X509Certificate2"/> for the Web UI's HTTPS listener. Prefers the
/// user-supplied PFX at <see cref="WebUiSettings.HttpsCertPath"/>; falls back to a self-signed
/// cert persisted at <c>paths.Root/webui-self-signed.pfx</c> so the same fingerprint is served
/// across restarts (important for clients that pin it manually).
/// </summary>
public static class WebUiCertificateProvider
{
    public const string SelfSignedFileName = "webui-self-signed.pfx";
    public const string SelfSignedSubject = "CN=WinBit WebUI";

    public static X509Certificate2 Resolve(WebUiSettings settings, Paths paths)
    {
        // EphemeralKeySet breaks Kestrel's TLS handshake on Windows; use the default (user key
        // set) flags with Exportable so the private key stays reachable for SChannel.
        const X509KeyStorageFlags flags = X509KeyStorageFlags.Exportable;

        if (!string.IsNullOrWhiteSpace(settings.HttpsCertPath) && File.Exists(settings.HttpsCertPath))
        {
            return new X509Certificate2(settings.HttpsCertPath, settings.HttpsCertPassword, flags);
        }

        var path = Path.Combine(paths.Root, SelfSignedFileName);
        if (!File.Exists(path))
        {
            var pfx = CreateSelfSignedPfx();
            File.WriteAllBytes(path, pfx);
        }
        return new X509Certificate2(path, password: (string?)null, flags);
    }

    /// <summary>Exposed so tests — and any future "regenerate cert" UI action — can produce a fresh PFX.</summary>
    public static byte[] CreateSelfSignedPfx()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(SelfSignedSubject, rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var sans = new SubjectAlternativeNameBuilder();
        sans.AddDnsName("localhost");
        sans.AddIpAddress(IPAddress.Loopback);
        sans.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(sans.Build());

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") /* server auth */ }, true));

        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(5));
        return cert.Export(X509ContentType.Pkcs12);
    }
}
