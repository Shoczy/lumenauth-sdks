using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace LumenAuth
{
    /// <summary>
    /// Official C# SDK for LumenAuth — license auth, HWID locking and
    /// license-gated downloads for desktop programs.
    ///
    /// Bind your app with three values — name + ownerid + version — no URL and
    /// no secret in your code:
    ///
    /// <code>
    /// using var auth = new LumenAuthClient("My app", "aBcD3fGh1J", "1.0", "lumsk_...", Hwid.Get());
    /// await auth.InitAsync();                       // resolve the app + check version
    /// await auth.LicenseAsync("LUMEN-XXXXX-...");   // or RegisterAsync / LoginAsync
    /// if (!await auth.CheckAsync()) Environment.Exit(1);
    /// byte[] bytes = await auth.FileAsync("&lt;file-id&gt;");
    /// </code>
    /// </summary>
    public sealed class LumenAuthClient : IDisposable
    {
        private const string DefaultUrl = "https://api.lumenauth.dev";

        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly string _baseUrl;
        private readonly string _name;
        private readonly string _ownerId;
        private readonly string _version;
        private readonly string _secret;
        private readonly string _hwid;

        // The hidden app-session minted by InitAsync().
        private string _appSession;

        /// <summary>The current user session id, or null when logged out.</summary>
        public string Session { get; private set; }

        /// <summary>The current end-user JWT, or null when logged out.</summary>
        public string Token { get; private set; }

        /// <summary>The signed-in user, or null when logged out.</summary>
        public LumenUser User { get; private set; }

        /// <param name="name">Application name (Manage Apps).</param>
        /// <param name="ownerId">Your fixed account owner id (Manage Apps).</param>
        /// <param name="version">App version; a mismatch is rejected at init.</param>
        /// <param name="hwid">Optional hardware id, sent with every auth call.</param>
        /// <param name="url">Override the API URL (self-hosting / dev). Normally omit.</param>
        /// <param name="httpClient">Reuse your own HttpClient. Otherwise one is created and disposed.</param>
        public LumenAuthClient(
            string name,
            string ownerId,
            string version,
            string secret,
            string hwid = null,
            string url = null,
            HttpClient httpClient = null)
        {
            if (string.IsNullOrEmpty(name)) throw new LumenAuthException("name is required");
            if (string.IsNullOrEmpty(ownerId)) throw new LumenAuthException("ownerid is required");
            if (string.IsNullOrEmpty(version)) throw new LumenAuthException("version is required");
            if (string.IsNullOrEmpty(secret)) throw new LumenAuthException("secret is required");

            _name = name;
            _ownerId = ownerId;
            _version = version;
            _secret = secret;
            _hwid = hwid;
            _baseUrl = (url ?? DefaultUrl).TrimEnd('/');

            if (httpClient != null)
            {
                _http = httpClient;
                _ownsHttp = false;
            }
            else
            {
                _http = new HttpClient();
                _ownsHttp = true;
            }
        }

        /// <summary>Resolve the app and check the version. Call once at startup.</summary>
        public async Task InitAsync(CancellationToken ct = default)
        {
            var res = await PostAsync(
                "/api/1.x/init",
                new Dictionary<string, object>
                {
                    ["name"] = _name,
                    ["ownerid"] = _ownerId,
                    ["version"] = _version,
                    ["secret"] = _secret,
                },
                authed: false,
                ct).ConfigureAwait(false);
            _appSession = res.GetProperty("session").GetString();
        }

        /// <summary>Register a new user by redeeming a license key.</summary>
        public Task<LumenUser> RegisterAsync(
            string email,
            string password,
            string license,
            string hwid = null,
            CancellationToken ct = default)
            => AuthAsync(
                "/api/1.x/register",
                new Dictionary<string, object>
                {
                    ["email"] = email,
                    ["password"] = password,
                    ["license"] = license,
                    ["hwid"] = hwid ?? _hwid,
                },
                ct);

        /// <summary>Log in with email + password (HWID-locked if a hwid is set).</summary>
        public Task<LumenUser> LoginAsync(
            string email,
            string password,
            string hwid = null,
            CancellationToken ct = default)
            => AuthAsync(
                "/api/1.x/login",
                new Dictionary<string, object>
                {
                    ["email"] = email,
                    ["password"] = password,
                    ["hwid"] = hwid ?? _hwid,
                },
                ct);

        /// <summary>Log in with just a license key.</summary>
        public Task<LumenUser> LicenseAsync(
            string license,
            string hwid = null,
            CancellationToken ct = default)
            => AuthAsync(
                "/api/1.x/license",
                new Dictionary<string, object>
                {
                    ["license"] = license,
                    ["hwid"] = hwid ?? _hwid,
                },
                ct);

        private async Task<LumenUser> AuthAsync(
            string path,
            Dictionary<string, object> body,
            CancellationToken ct)
        {
            await EnsureInitAsync(ct).ConfigureAwait(false);
            var res = await PostAsync(path, body, authed: true, ct).ConfigureAwait(false);
            Session = res.GetProperty("session").GetString();
            Token = res.GetProperty("token").GetString();
            User = JsonSerializer.Deserialize<LumenUser>(res.GetProperty("user").GetRawText());
            return User;
        }

        /// <summary>Returns true if the current session is still valid, else false.</summary>
        public async Task<bool> CheckAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(Session)) return false;
            await EnsureInitAsync(ct).ConfigureAwait(false);
            try
            {
                var res = await PostAsync(
                    "/api/1.x/check",
                    new Dictionary<string, object> { ["session"] = Session },
                    authed: true,
                    ct).ConfigureAwait(false);
                User = JsonSerializer.Deserialize<LumenUser>(res.GetProperty("user").GetRawText());
                return true;
            }
            catch (LumenAuthException ex) when (ex.Status.HasValue && ex.Status.Value < 500)
            {
                Logout();
                return false;
            }
        }

        /// <summary>Download a license-gated file. Returns the raw bytes.</summary>
        public async Task<byte[]> FileAsync(string fileId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(Session)) throw new LumenAuthException("Not logged in", 401);
            await EnsureInitAsync(ct).ConfigureAwait(false);

            using (var req = BuildRequest(
                "/api/1.x/file",
                new Dictionary<string, object> { ["session"] = Session, ["fileId"] = fileId },
                authed: true))
            {
                HttpResponseMessage resp;
                try
                {
                    resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                }
                catch (HttpRequestException e)
                {
                    throw new LumenAuthException($"Could not reach LumenAuth at {_baseUrl}: {e.Message}");
                }
                using (resp)
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        var err = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw ErrorFromText(err, (int)resp.StatusCode);
                    }
                    return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Read a level-gated server variable by name. The value is only
        /// returned if the logged-in user's level is high enough, so secrets
        /// (download URLs, config, feature flags) stay off the client.
        /// </summary>
        public async Task<string> VarAsync(string name, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(Session)) throw new LumenAuthException("Not logged in", 401);
            await EnsureInitAsync(ct).ConfigureAwait(false);
            var res = await PostAsync(
                "/api/1.x/var",
                new Dictionary<string, object> { ["session"] = Session, ["name"] = name },
                authed: true,
                ct).ConfigureAwait(false);
            return res.GetProperty("value").GetString();
        }

        /// <summary>Forget the current session locally.</summary>
        public void Logout()
        {
            Session = null;
            Token = null;
            User = null;
        }

        private async Task EnsureInitAsync(CancellationToken ct)
        {
            if (_appSession == null) await InitAsync(ct).ConfigureAwait(false);
        }

        private HttpRequestMessage BuildRequest(
            string path,
            Dictionary<string, object> body,
            bool authed)
        {
            // Drop null values so optional fields (hwid) are simply omitted.
            var filtered = new Dictionary<string, object>();
            foreach (var kv in body)
            {
                if (kv.Value != null) filtered[kv.Key] = kv.Value;
            }

            var json = JsonSerializer.Serialize(filtered);
            var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            if (authed && _appSession != null)
            {
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _appSession);
            }
            return req;
        }

        private async Task<JsonElement> PostAsync(
            string path,
            Dictionary<string, object> body,
            bool authed,
            CancellationToken ct)
        {
            using (var req = BuildRequest(path, body, authed))
            {
                HttpResponseMessage resp;
                try
                {
                    resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                }
                catch (HttpRequestException e)
                {
                    throw new LumenAuthException($"Could not reach LumenAuth at {_baseUrl}: {e.Message}");
                }
                using (resp)
                {
                    var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                    {
                        throw ErrorFromText(text, (int)resp.StatusCode);
                    }
                    using (var doc = JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text))
                    {
                        return doc.RootElement.Clone();
                    }
                }
            }
        }

        private static LumenAuthException ErrorFromText(string text, int status)
        {
            string message = null;
            try
            {
                using (var doc = JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text))
                {
                    if (doc.RootElement.TryGetProperty("message", out var m))
                        message = m.GetString();
                    else if (doc.RootElement.TryGetProperty("error", out var e))
                        message = e.GetString();
                }
            }
            catch (JsonException)
            {
                // Non-JSON body — fall back to a generic message.
            }
            return new LumenAuthException(message ?? $"Request failed ({status})", status);
        }

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }
    }

    /// <summary>An authenticated end user of your app.</summary>
    public sealed class LumenUser
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("email")] public string Email { get; set; }
        [JsonPropertyName("level")] public int Level { get; set; }
        [JsonPropertyName("expiresAt")] public string ExpiresAt { get; set; }
        [JsonPropertyName("createdAt")] public string CreatedAt { get; set; }
    }

    /// <summary>
    /// Raised on any failed LumenAuth call. <see cref="Status"/> is the HTTP
    /// status code, when known.
    /// </summary>
    public sealed class LumenAuthException : Exception
    {
        public int? Status { get; }

        public LumenAuthException(string message, int? status = null) : base(message)
        {
            Status = status;
        }
    }
}
