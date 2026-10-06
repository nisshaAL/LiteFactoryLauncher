using LiteFactoryLauncher.Models;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LiteFactoryLauncher.Services;

public sealed class LiteFactoryAuthService
{
    private const string ApiBaseUrlEnvironmentVariable = "LITEFACTORY_API_BASE_URL";
    private const string ProductionBaseUrl = "https://litefactoryapi.onrender.com";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    public static readonly Uri DefaultBaseAddress = ResolveBaseAddress();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public LiteFactoryAuthService()
        : this(new HttpClient { BaseAddress = DefaultBaseAddress, Timeout = RequestTimeout })
    {
    }

    public LiteFactoryAuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<LiteFactoryAuthResult> LoginAsync(string login, string password, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await PostJsonAsync("api/auth/login", new LoginRequest(login, password), cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return LiteFactoryAuthResult.Error("Invalid nickname or password.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return LiteFactoryAuthResult.Error(await ReadApiErrorAsync(response, "Login failed.", cancellationToken));
            }

            var authResponse = await ReadJsonAsync<AuthResponse>(response, cancellationToken);
            if (authResponse == null || string.IsNullOrWhiteSpace(authResponse.AccessToken))
            {
                return LiteFactoryAuthResult.Error("Login response from LiteFactory API was invalid.");
            }

            var account = await GetAccountAsync(authResponse.AccessToken, cancellationToken);
            if (account == null)
            {
                return LiteFactoryAuthResult.Error("Could not load LiteFactory account information.");
            }

            return LiteFactoryAuthResult.Ok(new LiteFactorySession
            {
                AccessToken = authResponse.AccessToken,
                ExpiresAtUtc = authResponse.ExpiresAtUtc,
                Account = account
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return LiteFactoryAuthResult.Error("LiteFactory API is unavailable. Check that the local API is running.");
        }
        catch (JsonException)
        {
            return LiteFactoryAuthResult.Error("LiteFactory API returned an invalid response.");
        }
    }

    public async Task<LiteFactoryAuthResult> RegisterAsync(
        string email,
        string nickname,
        string password,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await PostJsonAsync("api/auth/register", new RegisterRequest(email, nickname, password), cancellationToken);
            if (response.StatusCode == HttpStatusCode.Conflict || response.StatusCode == HttpStatusCode.BadRequest)
            {
                return LiteFactoryAuthResult.Error(await ReadApiErrorAsync(response, "Registration failed.", cancellationToken));
            }

            if (!response.IsSuccessStatusCode)
            {
                return LiteFactoryAuthResult.Error(await ReadApiErrorAsync(response, "Registration failed.", cancellationToken));
            }

            return await LoginAsync(nickname, password, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return LiteFactoryAuthResult.Error("LiteFactory API is unavailable. Check that the local API is running.");
        }
        catch (JsonException)
        {
            return LiteFactoryAuthResult.Error("LiteFactory API returned an invalid response.");
        }
    }

    private async Task<LiteFactoryAccount?> GetAccountAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/account/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await ReadJsonAsync<LiteFactoryAccount>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> PostJsonAsync<T>(string path, T payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _httpClient.PostAsync(path, content, cancellationToken);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static async Task<string> ReadApiErrorAsync(HttpResponseMessage response, string fallback, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var error = await JsonSerializer.DeserializeAsync<ApiErrorResponse>(stream, JsonOptions, cancellationToken);
            return string.IsNullOrWhiteSpace(error?.Error) ? fallback : error.Error;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static Uri ResolveBaseAddress()
    {
        var configuredBaseUrl = Environment.GetEnvironmentVariable(ApiBaseUrlEnvironmentVariable);
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? ProductionBaseUrl
            : configuredBaseUrl.Trim();

        if (!Uri.TryCreate(EnsureTrailingSlash(baseUrl), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("LiteFactory API base URL must be an absolute HTTP or HTTPS URL.");
        }

        return uri;
    }

    private static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";
    }

    private sealed record LoginRequest(string Login, string Password);

    private sealed record RegisterRequest(string Email, string Nickname, string Password);

    private sealed class AuthResponse
    {
        public string AccessToken { get; set; } = "";

        public DateTimeOffset ExpiresAtUtc { get; set; }
    }

    private sealed class ApiErrorResponse
    {
        public string Error { get; set; } = "";
    }
}
