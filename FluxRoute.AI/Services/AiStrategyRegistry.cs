using System.Text.Json;
using System.Text.Json.Serialization;
using FluxRoute.AI.Models;

namespace FluxRoute.AI.Services;

public sealed class BanditStateEntry
{
    public Guid GenomeId { get; set; }
    public string NetworkHash { get; set; } = "";
    public double Alpha { get; set; } = 1;
    public double Beta { get; set; } = 1;
}

public sealed class AiStrategyPersistedModel
{
    public List<StrategyGenome> Genomes { get; set; } = [];
    public List<BanditStateEntry> Bandit { get; set; } = [];
    public int GenerationCounter { get; set; }
    public List<string> SeenNetworkHashes { get; set; } = [];
}

public sealed class AiStrategyRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly object _gate = new();
    private AiStrategyPersistedModel _model = new();

    public AiStrategyRegistry(string path)
    {
        _path = path;
    }

    public int GenerationCounter
    {
        get { lock (_gate) return _model.GenerationCounter; }
        set { lock (_gate) _model.GenerationCounter = value; }
    }

    public void Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                _model = new AiStrategyPersistedModel();
                return;
            }

            try
            {
                var json = File.ReadAllText(_path);
                _model = JsonSerializer.Deserialize<AiStrategyPersistedModel>(json, JsonOptions) ?? new();
                _model.Genomes ??= [];
                _model.Bandit ??= [];
                _model.SeenNetworkHashes ??= [];
            }
            catch
            {
                _model = new AiStrategyPersistedModel();
            }
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_model, JsonOptions);
            var tmp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, json);
            File.Copy(tmp, _path, overwrite: true);
            File.Delete(tmp);
        }
    }

    public IReadOnlyList<StrategyGenome> GetGenomes()
    {
        lock (_gate)
            return _model.Genomes.ToList();
    }

    public IReadOnlyList<StrategyGenome> GetActiveGenomes()
    {
        lock (_gate)
            return _model.Genomes.Where(g => g.OrchestratorEnabled).ToList();
    }

    public void SetOrchestratorEnabled(Guid id, bool enabled)
    {
        lock (_gate)
        {
            var g = _model.Genomes.FirstOrDefault(x => x.Id == id);
            if (g is null)
                return;
            g.OrchestratorEnabled = enabled;
        }

        Save();
    }

    public StrategyGenome? GetById(Guid id)
    {
        lock (_gate)
            return _model.Genomes.FirstOrDefault(g => g.Id == id);
    }

    public void Upsert(StrategyGenome g)
    {
        lock (_gate)
        {
            var idx = _model.Genomes.FindIndex(x => x.Id == g.Id);
            if (idx >= 0)
                _model.Genomes[idx] = g;
            else
                _model.Genomes.Add(g);
        }
    }

    public bool Remove(Guid id)
    {
        lock (_gate)
        {
            var n = _model.Genomes.RemoveAll(g => g.Id == id);
            _model.Bandit.RemoveAll(b => b.GenomeId == id);
            return n > 0;
        }
    }

    public void MarkNetworkSeen(string hash)
    {
        lock (_gate)
        {
            if (!_model.SeenNetworkHashes.Contains(hash))
                _model.SeenNetworkHashes.Add(hash);
        }
    }

    public bool HasSeenNetwork(string hash)
    {
        lock (_gate)
            return _model.SeenNetworkHashes.Contains(hash);
    }

    public BanditStateEntry GetOrCreateBandit(Guid genomeId, string networkHash)
    {
        lock (_gate)
        {
            var e = _model.Bandit.FirstOrDefault(b => b.GenomeId == genomeId && b.NetworkHash == networkHash);
            if (e is not null)
                return e;

            e = new BanditStateEntry { GenomeId = genomeId, NetworkHash = networkHash, Alpha = 1, Beta = 1 };
            _model.Bandit.Add(e);
            return e;
        }
    }

    public void RecordBanditSuccess(Guid genomeId, string networkHash)
    {
        lock (_gate)
        {
            var e = _model.Bandit.FirstOrDefault(b => b.GenomeId == genomeId && b.NetworkHash == networkHash);
            if (e is null)
            {
                e = new BanditStateEntry { GenomeId = genomeId, NetworkHash = networkHash, Alpha = 1, Beta = 1 };
                _model.Bandit.Add(e);
            }

            e.Alpha += 1;
        }
    }

    public void RecordBanditFailure(Guid genomeId, string networkHash)
    {
        lock (_gate)
        {
            var e = _model.Bandit.FirstOrDefault(b => b.GenomeId == genomeId && b.NetworkHash == networkHash);
            if (e is null)
            {
                e = new BanditStateEntry { GenomeId = genomeId, NetworkHash = networkHash, Alpha = 1, Beta = 1 };
                _model.Bandit.Add(e);
            }

            e.Beta += 1;
        }
    }

    public double SumPullsForGenomeOnNetwork(Guid genomeId, string networkHash)
    {
        lock (_gate)
        {
            var e = _model.Bandit.FirstOrDefault(b => b.GenomeId == genomeId && b.NetworkHash == networkHash);
            if (e is null)
                return 0;
            return e.Alpha + e.Beta - 2;
        }
    }

    public (double Alpha, double Beta) GetAggregatedBeta(Guid genomeId)
    {
        lock (_gate)
        {
            double succ = 0;
            double fail = 0;
            foreach (var x in _model.Bandit.Where(t => t.GenomeId == genomeId))
            {
                succ += x.Alpha - 1;
                fail += x.Beta - 1;
            }

            return (succ + 1, fail + 1);
        }
    }

    public double TotalPullsOnNetwork(string networkHash)
    {
        lock (_gate)
        {
            double n = 0;
            foreach (var x in _model.Bandit.Where(t => t.NetworkHash == networkHash))
                n += x.Alpha + x.Beta - 2;
            return Math.Max(n, 0);
        }
    }

    public void ResetAll()
    {
        lock (_gate)
        {
            _model = new AiStrategyPersistedModel();
        }

        Save();
    }
}
