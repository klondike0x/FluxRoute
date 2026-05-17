using System.Globalization;
using System.Text;
using FluxRoute.AI.Models;
using FluxRoute.Core.Services;

namespace FluxRoute.AI.Services;

public sealed class BatMaterializer
{
    public string BuildBatContent(StrategyGenome g, string engineDir)
    {
        ProfileBatLauncher.PrepareRuntime(engineDir);
        var bin = Path.Combine(engineDir, "bin") + Path.DirectorySeparatorChar;
        var lists = Path.Combine(engineDir, "lists") + Path.DirectorySeparatorChar;
        var (gameFilter, gameTcp, gameUdp) = ReadGameFilter(engineDir);

        var args = BuildWinwsArgs(g);
        var argLine = string.Join(" ", args.Select(QuoteIfNeeded));

        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine($"set \"BIN={bin}\"");
        sb.AppendLine($"set \"LISTS={lists}\"");
        sb.AppendLine($"set \"GameFilter={gameFilter}\"");
        sb.AppendLine($"set \"GameFilterTCP={gameTcp}\"");
        sb.AppendLine($"set \"GameFilterUDP={gameUdp}\"");
        sb.AppendLine($"\"%BIN%winws.exe\" {argLine}");
        return sb.ToString();
    }

    public string WriteBat(StrategyGenome g, string engineDir)
    {
        var dir = Path.Combine(engineDir, "ai-evolved");
        Directory.CreateDirectory(dir);
        var safeName = SanitizeFileName(g.DisplayName);
        var path = Path.Combine(dir, $"{safeName}.bat");
        File.WriteAllText(path, BuildBatContent(g, engineDir), new UTF8Encoding(false));
        return path;
    }

    public static IReadOnlyList<string> BuildWinwsArgs(StrategyGenome g)
    {
        var list = new List<string>();

        if (!string.IsNullOrWhiteSpace(g.FilterTcp))
        {
            list.Add("--filter-tcp");
            list.Add(g.FilterTcp);
        }

        if (!string.IsNullOrWhiteSpace(g.FilterUdp))
        {
            list.Add("--filter-udp");
            list.Add(g.FilterUdp);
        }

        list.Add("--dpi-desync");
        list.Add(g.DesyncMode);

        if (g.SplitPosSemantic is not null)
        {
            list.Add("--dpi-desync-split-pos");
            list.Add(g.SplitPosSemantic);
        }
        else if (g.SplitPos is not null)
        {
            list.Add("--dpi-desync-split-pos");
            list.Add(g.SplitPos.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(g.FakeTlsMod))
        {
            list.Add("--dpi-desync-fake-tls-mod");
            list.Add(g.FakeTlsMod);
        }

        if (g.FakeTtl is not null)
        {
            list.Add("--dpi-desync-ttl");
            list.Add(g.FakeTtl.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (g.AutoTtl)
            list.Add("--dpi-desync-autottl");

        if (g.RepeatCount is not null)
        {
            list.Add("--dpi-desync-repeats");
            list.Add(g.RepeatCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(g.Hostlist))
        {
            list.Add("--hostlist");
            list.Add(g.Hostlist);
        }

        list.Add("--new");

        foreach (var x in g.ExtraArgs)
        {
            if (!string.IsNullOrWhiteSpace(x))
                list.Add(x);
        }

        return list;
    }

    private static (string GameFilter, string Tcp, string Udp) ReadGameFilter(string engineDir)
    {
        var flagPath = Path.Combine(engineDir, "utils", "game_filter.enabled");
        if (!File.Exists(flagPath))
            return ("12", "12", "12");

        var mode = File.ReadLines(flagPath).FirstOrDefault()?.Trim().ToLowerInvariant();
        return mode switch
        {
            "all" => ("1024-65535", "1024-65535", "1024-65535"),
            "tcp" => ("1024-65535", "1024-65535", "12"),
            "udp" => ("1024-65535", "12", "1024-65535"),
            _ => ("1024-65535", "1024-65535", "1024-65535"),
        };
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);
        var s = sb.ToString().Trim();
        return string.IsNullOrEmpty(s) ? "strategy" : s[..Math.Min(s.Length, 80)];
    }

    private static string QuoteIfNeeded(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
            return "\"\"";
        return arg.Any(char.IsWhiteSpace) ? $"\"{arg}\"" : arg;
    }
}
