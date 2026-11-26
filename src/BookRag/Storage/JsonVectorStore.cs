using System.Text.Json;
using System.Text.Json.Serialization;
using BookRag.Models;

namespace BookRag.Storage;

/// <summary>
/// Stores the vector index as a JSON file on disk.
/// Each collection maps to its own file: {dataDirectory}/{collectionName}.json
///
/// A companion metadata file ({collectionName}.meta.json) records the
/// embedding model name so the query command can warn when the loaded index
/// was built with a different model.
/// </summary>
public sealed class JsonVectorStore : IVectorStore
{
    private readonly string _filePath;
    private readonly string _metaPath;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        Converters    = { new JsonStringEnumConverter() }
    };

    public JsonVectorStore(string dataDirectory, string collectionName)
    {
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, $"{collectionName}.json");
        _metaPath = Path.Combine(dataDirectory, $"{collectionName}.meta.json");
    }

    /// <inheritdoc/>
    public async Task<List<Chunk>> LoadAsync()
    {
        if (!File.Exists(_filePath))
            return [];

        var json = await File.ReadAllTextAsync(_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return [];

        return JsonSerializer.Deserialize<List<Chunk>>(json, _jsonOptions) ?? [];
    }

    /// <inheritdoc/>
    public async Task SaveAsync(List<Chunk> chunks)
    {
        var json = JsonSerializer.Serialize(chunks, _jsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }

    /// <summary>
    /// Reads the embedding model name stored at ingest time.
    /// Returns null if the metadata file does not exist (old index).
    /// </summary>
    public async Task<string?> LoadEmbeddingModelAsync()
    {
        if (!File.Exists(_metaPath)) return null;

        var json = await File.ReadAllTextAsync(_metaPath);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("embeddingModel", out var v)
               ? v.GetString()
               : null;
    }

    /// <summary>
    /// Saves the embedding model name to the metadata sidecar.
    /// Called once at the end of a fresh ingest.
    /// </summary>
    public async Task SaveEmbeddingModelAsync(string modelName)
    {
        var json = JsonSerializer.Serialize(new { embeddingModel = modelName });
        await File.WriteAllTextAsync(_metaPath, json);
    }

    /// <summary>Full path to the backing JSON file (useful for logging).</summary>
    public string FilePath => _filePath;
}
