using GStudio.Common.Events;

namespace GStudio.Project.Store;

public sealed class EventLogStore
{
    public EventLogWriter OpenWriter(ProjectPaths paths)
    {
        return new EventLogWriter(paths);
    }

    public Task<IReadOnlyList<PointerEvent>> ReadPointerEventsAsync(ProjectPaths paths, CancellationToken cancellationToken = default)
    {
        return NdjsonStreamReader.ReadAllAsync<PointerEvent>(paths.PointerEventsPath, cancellationToken: cancellationToken);
    }

    public Task<IReadOnlyList<KeyboardEvent>> ReadKeyboardEventsAsync(ProjectPaths paths, CancellationToken cancellationToken = default)
    {
        return NdjsonStreamReader.ReadAllAsync<KeyboardEvent>(paths.KeyboardEventsPath, cancellationToken: cancellationToken);
    }

    public Task<IReadOnlyList<WindowEvent>> ReadWindowEventsAsync(ProjectPaths paths, CancellationToken cancellationToken = default)
    {
        return NdjsonStreamReader.ReadAllAsync<WindowEvent>(paths.WindowEventsPath, cancellationToken: cancellationToken);
    }
}
