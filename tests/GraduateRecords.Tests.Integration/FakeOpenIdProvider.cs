using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GraduateRecords.Tests.Integration;

/// <summary>
/// Minimal in-process OIDC provider for integration tests (AGENTS §22: resilience
/// against a fake <c>HttpMessageHandler</c>, no real university IdP, no network).
/// Serves discovery, JWKS, the token endpoint (RSA-signed id_token) and userinfo,
/// so the full Authorization Code + PKCE handshake runs end to end.
/// </summary>
public sealed class FakeOpenIdProvider : HttpMessageHandler
{
    public const string Authority = "https://idp.test";
    public const string ClientId = "graduate-records-test";
    public const string KeyId = "test-key";

    private readonly RSA _rsa = RSA.Create(2048);
    private int _disposed;

    /// <summary>The National ID claim returned by the userinfo endpoint.</summary>
    public string UserInfoNationalId { get; set; } = "0012345679";

    /// <summary>The nonce taken from the authorize redirect; the id_token must echo it.</summary>
    public string? NextNonce { get; set; }

    /// <summary>Bodies of the token exchanges, so tests can prove PKCE was used.</summary>
    public List<string> TokenRequestBodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        switch (request.RequestUri!.AbsolutePath)
        {
            case "/.well-known/openid-configuration":
                return Json(new
                {
                    issuer = Authority,
                    authorization_endpoint = Authority + "/authorize",
                    token_endpoint = Authority + "/token",
                    userinfo_endpoint = Authority + "/userinfo",
                    jwks_uri = Authority + "/jwks.json",
                    end_session_endpoint = Authority + "/signout",
                    response_types_supported = new[] { "code" },
                    subject_types_supported = new[] { "public" },
                    id_token_signing_alg_values_supported = new[] { "RS256" },
                    scopes_supported = new[] { "openid", "profile" },
                });

            case "/jwks.json":
            {
                var parameters = _rsa.ExportParameters(false);
                return Json(new
                {
                    keys = new[]
                    {
                        new
                        {
                            kty = "RSA",
                            use = "sig",
                            kid = KeyId,
                            alg = "RS256",
                            n = Base64Url(parameters.Modulus!),
                            e = Base64Url(parameters.Exponent!),
                        },
                    },
                });
            }

            case "/token":
            {
                TokenRequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                return Json(new
                {
                    access_token = "fake-access-token",
                    token_type = "Bearer",
                    expires_in = 3600,
                    id_token = CreateIdToken(),
                });
            }

            case "/userinfo":
                return Json(new { sub = "fake-sso-user", id = UserInfoNationalId });

            default:
                return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private string CreateIdToken()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Base64Url(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT", kid = KeyId })));
        var payload = Base64Url(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new
            {
                iss = Authority,
                sub = "fake-sso-user",
                aud = ClientId,
                iat = now,
                exp = now + 3600,
                nonce = NextNonce,
            })));

        var signingInput = header + "." + payload;
        var signature = _rsa.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return signingInput + "." + Base64Url(signature);
    }

    private static HttpResponseMessage Json(object payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
    };

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _rsa.Dispose();
        }

        base.Dispose(disposing);
    }
}
