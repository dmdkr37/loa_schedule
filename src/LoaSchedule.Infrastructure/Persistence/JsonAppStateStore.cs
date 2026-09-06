using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using LoaSchedule.Domain.Models;

namespace LoaSchedule.Infrastructure.Persistence;

public sealed class JsonAppStateStore
{
    private const string DataFileName = "data.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonAppStateStore(string? filePath = null)
    {
        FilePath = filePath ?? BuildDefaultFilePath();
    }

    public string FilePath { get; }

    public async Task<AppState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return SeedDataFactory.CreateInitialState();
        }

        await using var stream = File.OpenRead(FilePath);
        var state = await JsonSerializer.DeserializeAsync<AppState>(
            stream,
            SerializerOptions,
            cancellationToken);

        return state ?? SeedDataFactory.CreateInitialState();
    }

    public async Task SaveAsync(AppState state, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = FilePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken);
        }

        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    private static string BuildDefaultFilePath()
    {
        return Path.Combine(AppContext.BaseDirectory, DataFileName);
    }
}
