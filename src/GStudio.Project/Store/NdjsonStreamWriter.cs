using System.Text;
using System.Text.Json;
using System.Threading;

namespace GStudio.Project.Store;

public sealed class NdjsonStreamWriter<T> : IAsyncDisposable
{
    private readonly StreamWriter _writer;
    private readonly JsonSerializerOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public NdjsonStreamWriter(string filePath, JsonSerializerOptions? options = null)
    {
        var parent = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(parent))
        {
            throw new ArgumentException("A writable parent directory is required.", nameof(filePath));
        }

        Directory.CreateDirectory(parent);

        var stream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 32 * 1024,
            useAsync: true);

        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _options = options ?? JsonSerialization.LineOptions;
    }

    public async Task WriteAsync(T item, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var line = JsonSerializer.Serialize(item, _options);
            await _writer.WriteLineAsync(line).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _writer.FlushAsync().ConfigureAwait(false);
            _writer.Dispose();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
