namespace GStudio.Project.Store;

public sealed record ProjectPaths(
    string RootDirectory,
    string ProjectFilePath,
    string CaptureDirectory,
    string CaptureFramesDirectory,
    string CaptureAudioDirectory,
    string MicrophoneAudioPath,
    string SystemAudioPath,
    string EventsDirectory,
    string PointerEventsPath,
    string KeyboardEventsPath,
    string WindowEventsPath,
    string EditsDirectory,
    string TimelinePath,
    string ZoomsPath,
    string MasksPath,
    string HighlightsPath,
    string CaptionsPath,
    string CacheDirectory,
    string ProxyDirectory,
    string RenderCacheDirectory)
{
    public static ProjectPaths Create(string sessionRoot)
    {
        var captureDirectory = Path.Combine(sessionRoot, "capture");
        var eventsDirectory = Path.Combine(sessionRoot, "events");
        var editsDirectory = Path.Combine(sessionRoot, "edits");
        var cacheDirectory = Path.Combine(sessionRoot, "cache");

        return new ProjectPaths(
            RootDirectory: sessionRoot,
            ProjectFilePath: Path.Combine(sessionRoot, "project.json"),
            CaptureDirectory: captureDirectory,
            CaptureFramesDirectory: Path.Combine(captureDirectory, "frames"),
            CaptureAudioDirectory: Path.Combine(captureDirectory, "audio"),
            MicrophoneAudioPath: Path.Combine(captureDirectory, "audio", "mic.wav"),
            SystemAudioPath: Path.Combine(captureDirectory, "audio", "system.wav"),
            EventsDirectory: eventsDirectory,
            PointerEventsPath: Path.Combine(eventsDirectory, "pointer.ndjson"),
            KeyboardEventsPath: Path.Combine(eventsDirectory, "keyboard.ndjson"),
            WindowEventsPath: Path.Combine(eventsDirectory, "windows.ndjson"),
            EditsDirectory: editsDirectory,
            TimelinePath: Path.Combine(editsDirectory, "timeline.json"),
            ZoomsPath: Path.Combine(editsDirectory, "zooms.json"),
            MasksPath: Path.Combine(editsDirectory, "masks.json"),
            HighlightsPath: Path.Combine(editsDirectory, "highlights.json"),
            CaptionsPath: Path.Combine(editsDirectory, "captions.srt"),
            CacheDirectory: cacheDirectory,
            ProxyDirectory: Path.Combine(cacheDirectory, "proxies"),
            RenderCacheDirectory: Path.Combine(cacheDirectory, "render_cache"));
    }

    public IEnumerable<string> AllDirectories()
    {
        yield return RootDirectory;
        yield return CaptureDirectory;
        yield return CaptureFramesDirectory;
        yield return CaptureAudioDirectory;
        yield return EventsDirectory;
        yield return EditsDirectory;
        yield return CacheDirectory;
        yield return ProxyDirectory;
        yield return RenderCacheDirectory;
    }
}
