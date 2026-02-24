using System.Text.Json;
using GStudio.Common.Configuration;
using GStudio.Common.Project;

namespace GStudio.Project.Store;

public sealed class ProjectSessionStore
{
    private readonly string _projectsRoot;

    public ProjectSessionStore(string projectsRoot)
    {
        if (string.IsNullOrWhiteSpace(projectsRoot))
        {
            throw new ArgumentException("Projects root is required.", nameof(projectsRoot));
        }

        _projectsRoot = projectsRoot;
        Directory.CreateDirectory(_projectsRoot);
    }

    public async Task<ProjectSession> CreateSessionAsync(
        SessionSettings settings,
        CancellationToken cancellationToken = default)
    {
        var sessionId = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..24];
        var sessionRoot = Path.Combine(_projectsRoot, $"{sessionId}.sswin");

        var paths = ProjectPaths.Create(sessionRoot);
        foreach (var directory in paths.AllDirectories())
        {
            Directory.CreateDirectory(directory);
        }

        var manifest = new ProjectManifest
        {
            SessionId = sessionId,
            CreatedUtc = DateTimeOffset.UtcNow,
            Settings = settings,
            CaptureStats = new CaptureStats()
        };

        await SaveManifestAsync(paths.ProjectFilePath, manifest, cancellationToken).ConfigureAwait(false);
        return new ProjectSession(sessionId, paths, manifest);
    }

    public async Task<ProjectManifest> LoadManifestAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            projectFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            useAsync: true);

        var manifest = await JsonSerializer.DeserializeAsync<ProjectManifest>(
            stream,
            JsonSerialization.DocumentOptions,
            cancellationToken).ConfigureAwait(false);

        if (manifest is null)
        {
            throw new InvalidDataException($"Failed to deserialize manifest at '{projectFilePath}'.");
        }

        return manifest;
    }

    public async Task CompleteSessionAsync(
        ProjectSession session,
        CaptureStats stats,
        CancellationToken cancellationToken = default)
    {
        var finalized = session.Manifest with
        {
            EndedUtc = DateTimeOffset.UtcNow,
            CaptureStats = stats
        };

        await SaveManifestAsync(session.Paths.ProjectFilePath, finalized, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SaveManifestAsync(
        string filePath,
        ProjectManifest manifest,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            useAsync: true);

        await JsonSerializer.SerializeAsync(
            stream,
            manifest,
            JsonSerialization.DocumentOptions,
            cancellationToken).ConfigureAwait(false);
    }
}
