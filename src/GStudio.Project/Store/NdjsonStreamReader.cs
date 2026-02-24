using System.Text;
using System.Text.Json;

namespace GStudio.Project.Store;

public static class NdjsonStreamReader
{
    public static async Task<IReadOnlyList<T>> ReadAllAsync<T>(
        string filePath,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return Array.Empty<T>();
        }

        var items = new List<T>();

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 32 * 1024,
            useAsync: true);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var effectiveOptions = options ?? JsonSerialization.LineOptions;

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var item = JsonSerializer.Deserialize<T>(line, effectiveOptions);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items;
    }
}
