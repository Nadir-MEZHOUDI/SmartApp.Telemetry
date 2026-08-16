using System.Text.Json;

namespace SmartApp.Telemetry.Client;

internal sealed class InstallationStore
{
    private readonly string filePath;
    private InstallationState state;

    public InstallationStore(TelemetryOptions options)
    {
        var root = string.IsNullOrWhiteSpace(options.StoragePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartAppTelemetry", SafeName(options.Application))
            : options.StoragePath;
        Directory.CreateDirectory(root);
        filePath = Path.Combine(root, "telemetry.json");
        state = LoadOrCreate();
    }

    public Guid InstallationId => state.InstallationId;
    public bool FirstStartedSent => state.FirstStartedSent;

    public void MarkFirstStartedSent()
    {
        state = state with { FirstStartedSent = true };
        Persist();
    }

    private InstallationState LoadOrCreate()
    {
        try
        {
            if (File.Exists(filePath))
            {
                var existing = JsonSerializer.Deserialize<InstallationState>(File.ReadAllText(filePath));
                if (existing is null || existing.InstallationId == Guid.Empty)
                    return Create();
                return existing;
            }
        }
        catch (Exception) when (File.Exists(filePath))
        {
            // A corrupt local telemetry file must never break the host application.
        }
        return Create();
    }

    private InstallationState Create()
    {
        var created = new InstallationState(Guid.CreateVersion7(), DateTime.UtcNow, false);
        state = created;
        Persist();
        return created;
    }

    private void Persist()
    {
        try
        {
            File.WriteAllText(filePath, JsonSerializer.Serialize(state));
        }
        catch (Exception)
        {
            // Telemetry storage is best effort.
        }
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalid.Contains(character) ? '_' : character));
    }

    private sealed record InstallationState(Guid InstallationId, DateTime CreatedAt, bool FirstStartedSent);
}
