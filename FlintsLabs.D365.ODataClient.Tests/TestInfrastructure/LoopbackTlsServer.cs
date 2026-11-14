using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace FlintsLabs.D365.ODataClient.Tests.TestInfrastructure;

// Test CA trust stays in memory; no certificate-store installation. Windows Schannel
// needs an imported temporary user private key, which is deleted when the certificate is disposed.
internal sealed class LoopbackTlsServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _shutdown = new(TimeSpan.FromSeconds(15));
    private readonly X509Certificate2 _serverCertificate;
    private readonly string _response;

    public X509Certificate2 RootCertificate { get; }
    public Uri Address { get; }
    public Task Completion { get; }
    public bool RequestReceived { get; private set; }
    public string RequestText { get; private set; } = string.Empty;

    public LoopbackTlsServer(bool wrongHost = false, bool expired = false,
        HttpStatusCode statusCode = HttpStatusCode.OK, Uri? location = null, string body = "OK")
    {
        _response = $"HTTP/1.1 {(int)statusCode} {statusCode}\r\n"
                    + (location is null ? string.Empty : $"Location: {location.AbsoluteUri}\r\n")
                    + $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
        var now = DateTimeOffset.UtcNow;
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest(
            "CN=D365 TLS test root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        RootCertificate = rootRequest.CreateSelfSigned(now.AddDays(-7), now.AddDays(7));

        using var serverKey = RSA.Create(2048);
        var serverRequest = new CertificateRequest(
            "CN=D365 TLS test server", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        var names = new SubjectAlternativeNameBuilder();
        if (wrongHost)
            names.AddDnsName("wrong.example.test");
        else
            names.AddIpAddress(IPAddress.Loopback);
        serverRequest.CertificateExtensions.Add(names.Build());
        using var certificate = serverRequest.Create(
            RootCertificate,
            now.AddDays(-2),
            expired ? now.AddDays(-1) : now.AddDays(1),
            RandomNumberGenerator.GetBytes(16));
        _serverCertificate = certificate.CopyWithPrivateKey(serverKey);
        if (OperatingSystem.IsWindows())
        {
            // Schannel cannot use the ephemeral key returned by CopyWithPrivateKey.
            // Do not use PersistKeySet: disposing the imported certificate cleans up its key.
            using var ephemeral = _serverCertificate;
            var pfx = ephemeral.Export(X509ContentType.Pkcs12);
            try
            {
#if NET9_0_OR_GREATER
                _serverCertificate = X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet);
#else
                _serverCertificate = new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.UserKeySet);
#endif
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pfx);
            }
        }

        _listener.Start();
        Address = new Uri($"https://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
        Completion = ServeAsync();
    }

    private async Task ServeAsync()
    {
        try
        {
            using var connection = await _listener.AcceptTcpClientAsync(_shutdown.Token);
            await using var stream = new SslStream(connection.GetStream());
            await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                // The client has the issuer in its in-memory CustomTrustStore. Sending only
                // the leaf avoids SslStreamCertificateContext installing intermediates on Windows.
                ServerCertificate = _serverCertificate,
                EnabledSslProtocols = SslProtocols.Tls12
            }, _shutdown.Token);

            var request = new StringBuilder();
            var buffer = new byte[1024];
            while (request.Length < 16 * 1024)
            {
                var read = await stream.ReadAsync(buffer, _shutdown.Token);
                if (read == 0)
                    return;
                RequestReceived = true;
                request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                var text = request.ToString();
                var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headerEnd >= 0)
                {
                    var lengthHeader = text[..headerEnd].Split("\r\n")
                        .FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                    var bodyLength = lengthHeader is null ? 0 : int.Parse(lengthHeader.Split(':')[1].Trim());
                    if (text.Length >= headerEnd + 4 + bodyLength)
                        break;
                }
            }

            RequestText = request.ToString();
            await stream.WriteAsync(
                Encoding.UTF8.GetBytes(_response),
                _shutdown.Token);
        }
        catch (AuthenticationException)
        {
            // Expected when the client rejects the server's certificate.
        }
        catch (IOException)
        {
            // The client may close the socket during certificate rejection.
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync();
        await Completion;
        _listener.Stop();
        _serverCertificate.Dispose();
        RootCertificate.Dispose();
        _shutdown.Dispose();
    }
}
