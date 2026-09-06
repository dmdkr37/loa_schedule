using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LoaSchedule.Infrastructure.LostArkApi;

public sealed class LostArkArmoryClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly Func<string?> _tokenProvider;
    private readonly Dictionary<string, CachedProfile> _cache = new(StringComparer.OrdinalIgnoreCase);

    public LostArkArmoryClient(Func<string?> tokenProvider)
        : this(new HttpClient
        {
            BaseAddress = new Uri("https://developer-lostark.game.onstove.com/"),
            Timeout = TimeSpan.FromSeconds(15)
        }, tokenProvider)
    {
    }

    public LostArkArmoryClient(HttpClient httpClient, Func<string?> tokenProvider)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
    }

    public void ClearCache() => _cache.Clear();

    public async Task<LostArkCharacterProfile> GetCharacterProfileAsync(
        string characterName,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = characterName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new LostArkApiException("캐릭터명을 입력하세요.");
        }

        if (_cache.TryGetValue(normalizedName, out var cached)
            && DateTimeOffset.UtcNow - cached.StoredAt < TimeSpan.FromMinutes(5))
        {
            return cached.Profile;
        }

        var token = NormalizeToken(_tokenProvider());
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new LostArkApiException("Lost Ark API 키를 먼저 저장하세요.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"armories/characters/{Uri.EscapeDataString(normalizedName)}/profiles");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", token);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new LostArkApiException(BuildErrorMessage(response.StatusCode));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var dto = await JsonSerializer.DeserializeAsync<LostArkCharacterProfileDto>(
            stream,
            SerializerOptions,
            cancellationToken);
        if (dto is null
            || string.IsNullOrWhiteSpace(dto.CharacterName)
            || string.IsNullOrWhiteSpace(dto.CharacterClassName))
        {
            throw new LostArkApiException("API 응답에서 캐릭터 프로필을 읽지 못했습니다.");
        }

        if (!TryParseItemLevel(dto.ItemAvgLevel, out var itemLevel))
        {
            throw new LostArkApiException($"아이템 레벨 형식을 읽지 못했습니다: {dto.ItemAvgLevel}");
        }

        var profile = new LostArkCharacterProfile(
            dto.CharacterName,
            dto.ServerName ?? string.Empty,
            dto.CharacterClassName,
            itemLevel);
        _cache[normalizedName] = new CachedProfile(profile, DateTimeOffset.UtcNow);
        return profile;
    }

    private static string NormalizeToken(string? token)
    {
        var normalized = token?.Trim() ?? string.Empty;
        return normalized.StartsWith("bearer ", StringComparison.OrdinalIgnoreCase)
            ? normalized[7..].Trim()
            : normalized;
    }

    private static bool TryParseItemLevel(string? value, out decimal itemLevel)
    {
        var normalized = value?.Replace(",", string.Empty).Trim();
        return decimal.TryParse(
            normalized,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out itemLevel);
    }

    private static string BuildErrorMessage(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "API 키가 올바르지 않거나 사용 권한이 없습니다.",
        HttpStatusCode.NotFound => "캐릭터를 찾지 못했습니다. 캐릭터명을 확인하세요.",
        HttpStatusCode.TooManyRequests => "API 요청 한도를 초과했습니다. 잠시 후 다시 시도하세요.",
        HttpStatusCode.ServiceUnavailable => "Lost Ark API가 점검 중이거나 일시적으로 사용할 수 없습니다.",
        _ => $"Lost Ark API 요청에 실패했습니다. HTTP {(int)statusCode}"
    };

    private sealed record LostArkCharacterProfileDto(
        string? ServerName,
        string? CharacterName,
        string? CharacterClassName,
        string? ItemAvgLevel);

    private sealed record CachedProfile(
        LostArkCharacterProfile Profile,
        DateTimeOffset StoredAt);
}

public sealed record LostArkCharacterProfile(
    string CharacterName,
    string ServerName,
    string ClassName,
    decimal ItemLevel);

public sealed class LostArkApiException(string message) : Exception(message);
