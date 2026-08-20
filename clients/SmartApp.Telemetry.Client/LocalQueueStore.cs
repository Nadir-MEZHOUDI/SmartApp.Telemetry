using System.Text;
using System.Text.Json;

namespace SmartApp.Telemetry.Client;

internal sealed record TelemetryEnvelope(string Kind, JsonElement Payload);

internal sealed class LocalQueueStore : IDisposable
{
    private readonly string filePath;
    private readonly int maxBytes;
    private readonly SemaphoreSlim gate = new(1, 1);

    public LocalQueueStore(TelemetryOptions options)
    {
        var root = string.IsNullOrWhiteSpace(options.StoragePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartAppTelemetry", SafeName(options.Application))
            : options.StoragePath;
        Directory.CreateDirectory(root);
        filePath = Path.Combine(root, "telemetry-queue.jsonl");
        maxBytes = options.MaxQueueBytes;
    }

    public async Task AppendAsync(IEnumerable<TelemetryEnvelope> envelopes, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using (var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read, 16 * 1024, useAsync: true))
            await using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                foreach (var envelope in envelopes)
                    await writer.WriteLineAsync(JsonSerializer.Serialize(envelope).AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
            }
            if (new FileInfo(filePath).Length > maxBytes) await TrimAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Never let disk telemetry failures escape into the host app.
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<TelemetryEnvelope>> ReadAndClearAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(filePath)) return [];
            var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
            File.Delete(filePath);
            return lines.Select(Parse).Where(x => x is not null).Cast<TelemetryEnvelope>().ToArray();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task TrimAsync(CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        var kept = new List<string>();
        var size = 0;
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var lineSize = Encoding.UTF8.GetByteCount(lines[index]) + Environment.NewLine.Length;
            if (size + lineSize > maxBytes) break;
            kept.Add(lines[index]);
            size += lineSize;
        }
        kept.Reverse();
        await File.WriteAllLinesAsync(filePath, kept, cancellationToken);
    }

    private static TelemetryEnvelope? Parse(string line)
    {
        try { return JsonSerializer.Deserialize<TelemetryEnvelope>(line); }
        catch (JsonException) { return null; }
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalid.Contains(character) ? '_' : character));
    }

    public void Dispose() => gate.Dispose();
}
