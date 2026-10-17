using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace CaloriesTracker.Web.Services;

/// <summary>Supabase project settings, read from wwwroot/appsettings.json.</summary>
public sealed class SupabaseOptions
{
    public string Url { get; set; } = "";

    /// <summary>The public "anon" / "publishable" key. Never put the service_role / secret key here.</summary>
    public string AnonKey { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(AnonKey);
}

/// <summary>Signed-in session as returned by Supabase Auth.</summary>
public sealed class SupabaseSession
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
    [JsonPropertyName("expires_at")] public long ExpiresAt { get; set; }
    [JsonPropertyName("user")] public SupabaseUser User { get; set; } = new();

    public bool IsExpiringSoon =>
        DateTimeOffset.FromUnixTimeSeconds(ExpiresAt) < DateTimeOffset.UtcNow.AddMinutes(1);
}

public sealed class SupabaseUser
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("email")] public string? Email { get; set; }
}

/// <summary>The user's cloud document.</summary>
public sealed record RemoteDocument(JsonNode Data, DateTimeOffset UpdatedAt);

/// <summary>Raised for any Supabase failure, with a message that can be shown to the user.</summary>
public sealed class SupabaseException(string message) : Exception(message);

/// <summary>
/// Minimal Supabase client over the REST APIs (Auth + PostgREST), so no extra NuGet package is needed.
/// Sign-in uses an email one-time code: iOS home-screen apps do not share storage with Safari,
/// so a magic link would sign in the wrong place.
/// </summary>
public sealed class SupabaseClient(SupabaseOptions options, IJSRuntime js)
{
    private const string SessionKey = "calories-tracker:session";
    private const string Table = "user_data";

    private readonly HttpClient _http = new();
    private SupabaseSession? _session;
    private bool _sessionLoaded;

    public bool IsConfigured => options.IsConfigured;

    public string? Email => _session?.User.Email;

    public async Task<bool> IsSignedInAsync()
    {
        await EnsureSessionLoadedAsync();
        return _session is not null;
    }

    /// <summary>Signs in with email + password (account created in the Supabase dashboard, no email needed).</summary>
    public async Task SignInWithPasswordAsync(string email, string password)
    {
        var response = await SendAsync(HttpMethod.Post, "auth/v1/token?grant_type=password",
            new { email = email.Trim(), password }, authorized: false);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            // Supabase answers 400 "Invalid login credentials" for a wrong email or password.
            throw new SupabaseException("Courriel ou mot de passe invalide.");
        }
        await EnsureSuccessAsync(response, "Connexion impossible");
        await StoreSessionAsync(await ReadSessionAsync(response));
    }

    /// <summary>Emails a one-time code (creates the account on first use).</summary>
    public async Task SendCodeAsync(string email)
    {
        var response = await SendAsync(HttpMethod.Post, "auth/v1/otp",
            new { email = email.Trim(), create_user = true }, authorized: false);
        await EnsureSuccessAsync(response, "Envoi du code impossible");
    }

    /// <summary>
    /// Exchanges what the user received by email for a session: either the 6-digit code,
    /// or the sign-in link itself (copied without opening it). Free Supabase projects using the
    /// default email sender cannot customize templates, and the default email only contains the link.
    /// </summary>
    public async Task VerifyCodeAsync(string email, string codeOrLink)
    {
        string input = codeOrLink.Trim();

        // The address Supabase redirects to after the link was opened already holds a session.
        if (TryParseSessionFragment(input, out var access, out var refresh, out var expiresAt))
        {
            await SignInWithTokensAsync(access, refresh, expiresAt);
            return;
        }

        if (TryParseLink(input, out var tokenHash, out var linkType))
        {
            // The link carries a hashed token; its "type" is magiclink (existing user) or signup (first time).
            await VerifyAsync([linkType, "email", "magiclink", "signup"],
                type => new { type, token_hash = tokenHash });
            return;
        }

        // "email" covers existing users; "signup" covers the very first confirmation on some projects.
        await VerifyAsync(["email", "signup"], type => new { type, email = email.Trim(), token = input });
    }

    /// <summary>Extracts token and type from a Supabase verify link (…/auth/v1/verify?token=…&amp;type=…).</summary>
    public static bool TryParseLink(string input, out string tokenHash, out string type)
    {
        tokenHash = "";
        type = "email";
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            return false;
        }

        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            string value = pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "";
            if (pair[0] == "token") tokenHash = value;
            else if (pair[0] == "type" && value.Length > 0) type = value;
        }
        return tokenHash.Length > 0;
    }

    /// <summary>
    /// Reads a session from a redirect URL fragment (…#access_token=…&amp;refresh_token=…&amp;expires_at=…).
    /// </summary>
    public static bool TryParseSessionFragment(string input, out string accessToken, out string refreshToken, out long expiresAt)
    {
        accessToken = refreshToken = "";
        expiresAt = 0;
        int hash = input.IndexOf('#');
        if (hash < 0)
        {
            return false;
        }

        foreach (var part in input[(hash + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            string value = pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "";
            switch (pair[0])
            {
                case "access_token": accessToken = value; break;
                case "refresh_token": refreshToken = value; break;
                case "expires_at": _ = long.TryParse(value, out expiresAt); break;
            }
        }
        return accessToken.Length > 0 && refreshToken.Length > 0;
    }

    /// <summary>Builds a session from tokens received in a redirect, fetching the user from Supabase.</summary>
    public async Task SignInWithTokensAsync(string accessToken, string refreshToken, long expiresAt)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{options.Url.TrimEnd('/')}/auth/v1/user");
        request.Headers.Add("apikey", options.AnonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new SupabaseException("Pas de connexion Internet.");
        }
        await EnsureSuccessAsync(response, "Lien de connexion invalide ou expiré");

        var user = await response.Content.ReadFromJsonAsync<SupabaseUser>() ?? new SupabaseUser();
        await StoreSessionAsync(new SupabaseSession
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt > 0 ? expiresAt : DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            User = user,
        });
    }

    // Tries each verification type in turn; the error of the last attempt is shown to the user.
    private async Task VerifyAsync(IEnumerable<string> types, Func<string, object> body)
    {
        HttpResponseMessage? last = null;
        foreach (var type in types.Distinct())
        {
            last = await SendAsync(HttpMethod.Post, "auth/v1/verify", body(type), authorized: false);
            if (last.IsSuccessStatusCode)
            {
                await StoreSessionAsync(await ReadSessionAsync(last));
                return;
            }
        }
        await EnsureSuccessAsync(last!, "Code ou lien invalide ou expiré");
    }

    public async Task SignOutAsync()
    {
        _session = null;
        await js.InvokeVoidAsync("localStorage.removeItem", SessionKey);
    }

    /// <summary>Reads the signed-in user's document, or null if none was saved yet.</summary>
    public async Task<RemoteDocument?> GetDocumentAsync()
    {
        var response = await SendAsync(HttpMethod.Get, $"rest/v1/{Table}?select=data,updated_at", null, authorized: true);
        await EnsureSuccessAsync(response, "Lecture du nuage impossible");

        var rows = await response.Content.ReadFromJsonAsync<JsonArray>() ?? [];
        if (rows.Count == 0 || rows[0] is not JsonObject row || row["data"] is not JsonNode data)
        {
            return null;
        }
        var updatedAt = row["updated_at"]!.GetValue<DateTimeOffset>();
        return new RemoteDocument(data.DeepClone(), updatedAt);
    }

    /// <summary>Creates or replaces the signed-in user's document.</summary>
    public async Task PutDocumentAsync(JsonNode data, DateTimeOffset updatedAt)
    {
        var userId = _session?.User.Id ?? throw new SupabaseException("Non connecté.");
        var body = new JsonArray(new JsonObject
        {
            ["user_id"] = userId,
            ["data"] = data.DeepClone(),
            ["updated_at"] = updatedAt.ToUniversalTime().ToString("O"),
        });

        var response = await SendAsync(HttpMethod.Post, $"rest/v1/{Table}?on_conflict=user_id", body, authorized: true,
            prefer: "resolution=merge-duplicates,return=minimal");
        await EnsureSuccessAsync(response, "Envoi vers le nuage impossible");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, object? body, bool authorized, string? prefer = null)
    {
        if (!IsConfigured)
        {
            throw new SupabaseException("Supabase n'est pas configuré (appsettings.json).");
        }
        if (authorized)
        {
            await EnsureFreshSessionAsync();
        }

        var request = new HttpRequestMessage(method, $"{options.Url.TrimEnd('/')}/{path}");
        request.Headers.Add("apikey", options.AnonKey);
        if (authorized && _session is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
        }
        if (prefer is not null)
        {
            request.Headers.Add("Prefer", prefer);
        }
        if (body is not null)
        {
            // Explicit length (no chunked encoding) for maximum proxy compatibility.
            request.Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");
        }

        try
        {
            return await _http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new SupabaseException("Pas de connexion Internet.");
        }
    }

    private async Task EnsureSessionLoadedAsync()
    {
        if (_sessionLoaded)
        {
            return;
        }
        _sessionLoaded = true;
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", SessionKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                _session = JsonSerializer.Deserialize<SupabaseSession>(json);
            }
        }
        catch (JsonException)
        {
            _session = null;
        }
    }

    private async Task EnsureFreshSessionAsync()
    {
        await EnsureSessionLoadedAsync();
        if (_session is null)
        {
            throw new SupabaseException("Non connecté.");
        }
        if (!_session.IsExpiringSoon)
        {
            return;
        }

        var response = await SendAsync(HttpMethod.Post, "auth/v1/token?grant_type=refresh_token",
            new { refresh_token = _session.RefreshToken }, authorized: false);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            // Refresh token revoked or expired: the user has to sign in again.
            await SignOutAsync();
            throw new SupabaseException("Session expirée, reconnecte-toi.");
        }
        await EnsureSuccessAsync(response, "Renouvellement de la session impossible");
        await StoreSessionAsync(await ReadSessionAsync(response));
    }

    private static async Task<SupabaseSession> ReadSessionAsync(HttpResponseMessage response)
    {
        var session = await response.Content.ReadFromJsonAsync<SupabaseSession>()
                      ?? throw new SupabaseException("Réponse de connexion vide.");
        if (session.ExpiresAt == 0)
        {
            // Some responses only carry expires_in; assume the default one-hour lifetime.
            session.ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        }
        return session;
    }

    private async Task StoreSessionAsync(SupabaseSession session)
    {
        _session = session;
        _sessionLoaded = true;
        await js.InvokeVoidAsync("localStorage.setItem", SessionKey, JsonSerializer.Serialize(session));
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string context)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        string detail = await response.Content.ReadAsStringAsync();
        throw new SupabaseException($"{context} ({(int)response.StatusCode}). {Shorten(ExtractMessage(detail))}".Trim());
    }

    // Supabase errors come as {"msg": ...}, {"message": ...} or {"error_description": ...}.
    private static string ExtractMessage(string body)
    {
        try
        {
            var node = JsonNode.Parse(body) as JsonObject;
            return node?["msg"]?.ToString() ?? node?["message"]?.ToString()
                   ?? node?["error_description"]?.ToString() ?? body;
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static string Shorten(string text) => text.Length > 160 ? text[..160] + "…" : text;
}
