using System.Threading;
using GStudio.Common.Events;

namespace GStudio.Project.Store;

public sealed class EventLogWriter : IAsyncDisposable
{
    private readonly NdjsonStreamWriter<PointerEvent> _pointerWriter;
    private readonly NdjsonStreamWriter<KeyboardEvent> _keyboardWriter;
    private readonly NdjsonStreamWriter<WindowEvent> _windowWriter;

    private int _pointerCount;
    private int _keyboardCount;
    private int _windowCount;

    internal EventLogWriter(ProjectPaths paths)
    {
        _pointerWriter = new NdjsonStreamWriter<PointerEvent>(paths.PointerEventsPath);
        _keyboardWriter = new NdjsonStreamWriter<KeyboardEvent>(paths.KeyboardEventsPath);
        _windowWriter = new NdjsonStreamWriter<WindowEvent>(paths.WindowEventsPath);
    }

    public int PointerCount => _pointerCount;

    public int KeyboardCount => _keyboardCount;

    public int WindowCount => _windowCount;

    public async Task WritePointerAsync(PointerEvent pointerEvent, CancellationToken cancellationToken = default)
    {
        await _pointerWriter.WriteAsync(pointerEvent, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _pointerCount);
    }

    public async Task WriteKeyboardAsync(KeyboardEvent keyboardEvent, CancellationToken cancellationToken = default)
    {
        await _keyboardWriter.WriteAsync(keyboardEvent, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _keyboardCount);
    }

    public async Task WriteWindowAsync(WindowEvent windowEvent, CancellationToken cancellationToken = default)
    {
        await _windowWriter.WriteAsync(windowEvent, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _windowCount);
    }

    public async ValueTask DisposeAsync()
    {
        await _pointerWriter.DisposeAsync().ConfigureAwait(false);
        await _keyboardWriter.DisposeAsync().ConfigureAwait(false);
        await _windowWriter.DisposeAsync().ConfigureAwait(false);
    }
}
