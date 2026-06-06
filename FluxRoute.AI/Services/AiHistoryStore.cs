using System.Text.Json;
using FluxRoute.AI.Models;

namespace FluxRoute.AI.Services;

public sealed class AiHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly string _path;
    private readonly object _gate = new();
    private List<ProbeOutcome>? _cache;

    public AiHistoryStore(string path)
    {
        _path = path;
    }

    public void Append(ProbeOutcome outcome)
    {
        var line = JsonSerializer.Serialize(outcome, JsonOptions) + Environment.NewLine;
        lock (_gate)
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(_path, line);

            if (_cache != null)
            {
                _cache.Add(outcome);
            }
            else
            {
                _cache = new List<ProbeOutcome> { outcome };
            }
        }
    }

    public List<ProbeOutcome> LoadRecent(TimeSpan window)
    {
        var cutoff = DateTimeOffset.UtcNow - window;
        return LoadAll().Where(o => o.Timestamp >= cutoff).ToList();
    }

    public List<ProbeOutcome> LoadFor(Guid genomeId, string networkHash)
    {
        return LoadAll().Where(o => o.GenomeId == genomeId && o.NetworkHash == networkHash).ToList();
    }

    public List<ProbeOutcome> LoadForNetwork(string networkHash)
    {
        return LoadAll().Where(o => o.NetworkHash == networkHash).ToList();
    }

    public List<ProbeOutcome> LoadAll()
    {
        lock (_gate)
        {
            if (_cache != null)
                return [.. _cache];

            if (!File.Exists(_path))
                return [];

            var list = new List<ProbeOutcome>();
            foreach (var raw in File.ReadLines(_path))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                    continue;
                try
                {
                    var o = JsonSerializer.Deserialize<ProbeOutcome>(line, JsonOptions);
                    if (o is not null)
                        list.Add(o);
                }
                catch
                {
                }
            }

            _cache = [.. list];
            return list;
        }
    }

    public void RotateOldEntries(int keepDays)
    {
        if (keepDays <= 0)
            return;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-keepDays);
        List<ProbeOutcome> kept;
        lock (_gate)
        {
            kept = LoadAll().Where(o => o.Timestamp >= cutoff).ToList();
        }

        lock (_gate)
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var fs = File.Create(_path);
            using var sw = new StreamWriter(fs);
            foreach (var o in kept.OrderBy(x => x.Timestamp))
                sw.WriteLine(JsonSerializer.Serialize(o, JsonOptions));

            _cache = [.. kept];
        }
    }
}
