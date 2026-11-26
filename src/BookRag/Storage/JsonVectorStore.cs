using System.Text.Json;
using System.Text.Json.Serialization;
using BookRag.Models;

namespace BookRag.Storage;

/// <summary>
/// Stores the vector index as a JSON file on disk.
/// Each collection maps to its own file: {dataDirectory}/{collectionName}.json
///
/// This is intentionally simple — a flat file is easy to inspect, copy,
/// and version-control (though large collections should be .gitignore'd).
/// For production usage with large corpora, swap this for a proper vector
/// database such as Qdrant or Chroma.
/// </summary>
public sealed class JsonVectorStore : IVectorStore
{
    private readonly string _filePath;

    // JSON options are shared across calls to avoid repeated allocation.
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,                     // compact on disk
        Converters    = { new JsonStringEnumConverter() }
    };

    public JsonVectorStore(string dataDirectory, string collectionName)
    {
        // Ensure the data directory exists so we can write to it.
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, $"{collectionName}.json");
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

    /// <summary>Full path to the backing JSON file (useful for logging).</summary>
    public string FilePath => _filePath;
}
