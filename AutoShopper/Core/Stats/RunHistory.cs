using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace AutoShopper.Core.Stats;

// Kept out of the main config so a long history can't bloat or corrupt the settings file. Disk access is
// best-effort: a failed read or write degrades to an empty or unsaved history instead of throwing.
internal sealed class RunHistory
{
    private const int MaxRecords = 500;
    private const string FileName = "run-history.json";
    private const string LegacyDryRunField = "DryRun";

    public RunHistory()
    {
        Load();
        RecomputeLifetime();
    }

    public List<RunRecord> Records { get; private set; } = [];

    public LifetimeTotals Lifetime { get; private set; }

    // Rises on every change, so text built from the totals is rebuilt only when they move.
    public int Revision { get; private set; }

    private static string FilePath
        => Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, FileName);

    public void Append(RunRecord record)
    {
        Records.Add(record);
        Records.Sort((left, right) => right.EndedAtUtc.CompareTo(left.EndedAtUtc));
        if (Records.Count > MaxRecords)
        {
            Records.RemoveRange(MaxRecords, Records.Count - MaxRecords);
        }

        RecomputeLifetime();
        Save();
    }

    // A row already in the history changed, such as the way home of a run reporting back.
    public void Update()
    {
        RecomputeLifetime();
        Save();
    }

    public void Clear()
    {
        Records.Clear();
        RecomputeLifetime();
        Save();
    }

    private void Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path))
            {
                return;
            }

            if (JToken.Parse(File.ReadAllText(path)) is not JArray rows)
            {
                return;
            }

            var dropped = DropDryRuns(rows);
            Records = rows.ToObject<List<RunRecord>>() ?? [];
            if (dropped > 0)
            {
                RunLog.Info($"RunHistory: dropped {dropped} dry run row(s), which bought nothing.");
                Save();
            }
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "RunHistory load failed; starting empty");
        }
    }

    // Rows written while dry runs existed would otherwise read as real spending.
    private static int DropDryRuns(JArray rows)
    {
        var dropped = 0;
        for (var index = rows.Count - 1; index >= 0; index--)
        {
            if (rows[index] is JObject row && row.Value<bool?>(LegacyDryRunField) == true)
            {
                rows.RemoveAt(index);
                dropped++;
            }
        }

        return dropped;
    }

    private void RecomputeLifetime()
    {
        var totals = new LifetimeTotals { Runs = Records.Count };
        var worldCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var topWorldCount = 0;
        for (var index = 0; index < Records.Count; index++)
        {
            var record = Records[index];
            totals.Seconds += record.DurationSeconds;
            if (record.Finished)
            {
                totals.Finished++;
            }

            if (record.WorldName.Length == 0)
            {
                continue;
            }

            worldCounts.TryGetValue(record.WorldName, out var count);
            worldCounts[record.WorldName] = ++count;
            if (count > topWorldCount)
            {
                topWorldCount = count;
                totals.TopWorld = record.WorldName;
                totals.TopDataCenter = record.DataCenterName;
            }
        }

        Lifetime = totals;
        Revision++;
    }

    private void Save()
    {
        try
        {
            var directory = Plugin.PluginInterface.ConfigDirectory;
            if (!directory.Exists)
            {
                directory.Create();
            }

            File.WriteAllText(FilePath, JsonConvert.SerializeObject(Records, Formatting.Indented));
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "RunHistory save failed");
        }
    }

    public struct LifetimeTotals
    {
        public int Runs;
        public int Finished;
        public double Seconds;
        public string? TopWorld;
        public string? TopDataCenter;

        public readonly TimeSpan Duration => TimeSpan.FromSeconds(Seconds);
        public readonly TimeSpan AverageDuration => Runs > 0 ? TimeSpan.FromSeconds(Seconds / Runs) : TimeSpan.Zero;
    }
}
