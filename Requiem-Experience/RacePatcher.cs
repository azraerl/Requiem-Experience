using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using MathNet.Numerics.Statistics;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Newtonsoft.Json.Linq;

namespace RequiemExperience
{
    [SupportedOSPlatform("windows7.0")]
    [SupportedOSPlatform("windows10")]
    class RacePatcher
    {
        public static bool RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, Settings Settings)
        {
            bool any = false;
            Console.WriteLine($@"Settings.RaceSettings.PatchRaces is {Settings.RacesSettings.PatchRaces}");
            if (!Settings.RacesSettings.PatchRaces)
            {
                return any;
            }

            StringBuilder? npcs = Settings.General.Debug ? new StringBuilder(500 * 1024) : null;
            npcs?.Append("NPC;Race;Level;Comments\r\n");

            Expressive.Expression exp = new(Settings.RacesSettings.LevelFormula, Expressive.ExpressiveOptions.IgnoreCaseAll);

            string settingsFile = state.ExtraSettingsDataPath + @"\RaceSettings.json";
            var racesOverrs = new Dictionary<string, int>();
            var ignoredNPCs = new List<Regex>();
            var uniqueNPCs = new List<Regex>();
            var raceGroups = new Dictionary<string, string[]>();
            if (!File.Exists(settingsFile))
            {
                Console.WriteLine("\"RaceSettings.json\" not located in Users Data folder.");
            }
            else
            {
                var settingJson = JObject.Parse(File.ReadAllText(settingsFile));
                var ignore = settingJson["Ignore"]?.ToObject<string[]>();
                if (ignore != null)
                {
                    ignoredNPCs.AddRange(
                        collection: ignore.Select(
                            x => new Regex("^" + x + "$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline)
                        )
                    );
                }
                var unique = settingJson["Unique"]?.ToObject<string[]>();
                if (unique != null)
                {
                    uniqueNPCs.AddRange(
                        collection: unique.Select(
                            x => new Regex("^" + x + "$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline)
                        )
                    );
                }
                racesOverrs = settingJson["Override"]?.ToObject<Dictionary<string, int>>() ?? racesOverrs;
                raceGroups = settingJson["Groups"]?.ToObject<Dictionary<string, string[]>>() ?? raceGroups;
            }

            Console.WriteLine($"Processing NPC Races:\r\n" +
                $" + Averaging mode is {Settings.RacesSettings.Mode}\r\n" +
                $" + Groups count is {raceGroups.Count}\r\n" +
                $" + Ignorable NPCs count is {ignoredNPCs.Count}\r\n" +
                $" + Unique NPCs count is {uniqueNPCs.Count}\r\n" +
                $" + Race XP overrides count is {racesOverrs.Count}\r\n" +
                $" + Level Expression is {Settings.RacesSettings.LevelFormula}\r\n" +
                $" + Debug = {npcs != null}");

            var racesLevels = new Dictionary<string, ICollection<double>>();
            var actorPlugins = new SortedDictionary<string, SortedSet<string>>();

            foreach (var npc in state.LoadOrder.PriorityOrder.WinningOverrides<INpcGetter>())
            {
                if (npc.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Stats))
                {
                    npcs?.Append(npc.EditorID + ",?,?,skipped\r\n");
                    continue;
                }

                var ignore = npc.EditorID == null || ignoredNPCs.Any(x => x.IsMatch(npc.EditorID));
                if (ignore)
                {
                    npcs?.Append(npc.EditorID + ",?,?,ignored\r\n");
                    continue;
                }

                int level = 0;
                if (npc.Configuration.Level is IPcLevelMult)
                {
                    level = npc.Configuration.CalcMinLevel;
                    if (npc.Configuration.CalcMaxLevel != 0)
                    {
                        level = (level + npc.Configuration.CalcMaxLevel) / 2;
                    }
                }
                else if (npc.Configuration.Level is INpcLevelGetter npcLevel)
                {
                    level = npcLevel.Level;
                }

                string? key = null;
                string? val = null;
                string? EditorID = null;
                var unique = npc.EditorID != null && uniqueNPCs.Any(x => x.IsMatch(npc.EditorID));

                if (npc.Race.TryResolve(state.LinkCache, out var race))
                {
                    EditorID = race.EditorID;
                    bool overridden = racesOverrs.Any(x => x.Key.Equals(race.EditorID, StringComparison.OrdinalIgnoreCase));
                    if (overridden)
                    {
                        npcs?.Append(npc.EditorID + "," + EditorID + "," + level + "," + "overridden" + "\r\n");
                        continue;
                    }

                    if (Settings.RacesSettings.NPCUniqueFlagThreshold > 0 &&
                        Settings.RacesSettings.NPCUniqueFlagThreshold <= level)
                    {
                        if (unique && npcs != null && npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Unique))
                        {
                            Console.WriteLine($"INFO: {npc.EditorID} is already unique, regex is an overhead");
                        }
                        unique |= npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Unique);
                    }
                    else
                    {
                        // filter away low level (or levelled) unique flagged NPCs
                        unique = false;
                    }

                    if (unique && level > 0)
                    {
                        if (!actorPlugins.ContainsKey(npc.FormKey.ModKey.ToString()))
                        {
                            actorPlugins.Add(npc.FormKey.ModKey.ToString(), new());
                        }
                        actorPlugins[npc.FormKey.ModKey.ToString()]
                            .Add($@"; {npc.EditorID} ""{npc.Name}"" | {race.EditorID} : 00{race.FormKey.IDString()}" +
                                 $@"{Environment.NewLine}00{npc.FormKey.IDString()}={Express(level, exp)}");
                        any = true;
                    }

                    if (level > 0 && !unique)
                    {
                        if (!unique && RaceGroupLookup(raceGroups, EditorID, out key, out val))
                        {
                            if (racesLevels.TryGetValue(key + val, out var levels))
                            {
                                levels.Add(level);
                            }
                            else
                            {
                                racesLevels.Add(key + val, new List<double>() { level });
                            }
                        }
                        else if (EditorID != null && racesLevels.TryGetValue(EditorID, out var levels))
                        {
                            levels.Add(level);
                        }
                        else if (EditorID != null)
                        {
                            racesLevels.Add(EditorID, new List<double>() { level });
                        }
                    }
                }
                npcs?.Append(npc.EditorID + "," + EditorID + "," + level + "," + key + val + (unique ? " unique" : "") + "\r\n");
            }

            var racesPlugins = new SortedDictionary<string, SortedSet<string>>();
            foreach (var race in state.LoadOrder.PriorityOrder.WinningOverrides<IRaceGetter>())
            {
                SortedSet<string> races;
                var modKey = race.FormKey.ModKey.ToString();
                if (racesPlugins.ContainsKey(modKey))
                {
                    races = racesPlugins[modKey];
                }
                else
                {
                    races = new();
                    racesPlugins.Add(modKey, races);
                }

                if (racesOverrs.TryGetValue(race.EditorID ?? "null", out int overrid))
                {
                    races.Add($@"; {race.EditorID} ""{race.Name}""{Environment.NewLine}00{race.FormKey.IDString()}={Express(overrid, exp)}");
                }
                else if (RaceGroupLookup(raceGroups, race.EditorID, out var key, out var val)
                    && racesLevels.TryGetValue(key + val, out var glevels) && glevels.Any())
                {
                    races.Add($@"; {race.EditorID} ""{race.Name}""{Environment.NewLine}00{race.FormKey.IDString()}={Express(Average(glevels, Settings.RacesSettings.Mode), exp)}");
                }
                else if (racesLevels.TryGetValue(race.EditorID ?? "null", out var levels) && levels.Any())
                {
                    races.Add($@"; {race.EditorID} ""{race.Name}""{Environment.NewLine}00{race.FormKey.IDString()}={Express(Average(levels, Settings.RacesSettings.Mode), exp)}");
                }
                else if (race.Starting.TryGetValue(BasicStat.Health, out var startingHealth))
                {
                    // fallback for races which don't have NPCs defined
                    races.Add($@"; {race.EditorID} ""{race.Name}""{Environment.NewLine}00{race.FormKey.IDString()}={Express(Math.Sqrt(startingHealth), exp)}");
                }
            }

            var racesOutput = new StringBuilder(100 * 1024); // 100 KiB
            foreach (var rk in racesPlugins)
            {
                racesOutput.AppendLine($@"[{rk.Key}]");
                foreach (var l in rk.Value) racesOutput.AppendLine(l);
                racesOutput.AppendLine();
            }
            var actorsOutput = new StringBuilder(100 * 1024);
            foreach (var ak in actorPlugins)
            {
                actorsOutput.AppendLine($@"[{ak.Key}]");
                foreach (var l in ak.Value) actorsOutput.AppendLine(l);
                actorsOutput.AppendLine();
            }


            var outputPath = $@"{state.DataFolderPath}\SKSE\Plugins\Experience\";
            Console.WriteLine($@"Creating folder: {outputPath}Races\");
            Directory.CreateDirectory($@"{outputPath}Races\");
            Console.WriteLine($@"Writing races patch: {outputPath}Races\{Settings.RacesSettings.OutputFile}");
            File.WriteAllText($@"{outputPath}Races\{Settings.RacesSettings.OutputFile}", racesOutput.ToString());
            Console.WriteLine($@"Writing actors patch: {outputPath}Actors\{Settings.RacesSettings.OutputFile}");
            File.WriteAllText($@"{outputPath}Actors\{Settings.RacesSettings.OutputFile}", actorsOutput.ToString());

            if (npcs != null)
            {
                Console.WriteLine($@"Writing debug file: {outputPath}npcs.csv");
                File.WriteAllText($@"{outputPath}npcs.csv", npcs.ToString());
            }

            return any;
        }

        static int Express(double level, Expressive.Expression ex)
        {
            if (ex == null)
            {
                return (int)level;
            }
            var result = ex.Evaluate(new Dictionary<string, object> { ["level"] = level });
            if (result is not double)
            {
                return Convert.ToInt32(result);
            }
            else
            {
                return Convert.ToInt32(Math.Ceiling((double)result));
            }
        }

        static double Average(ICollection<double> levels, RacesSettings.AverageMode mode)
        {
            double ret = double.NaN;
            ret = mode switch
            {
                RacesSettings.AverageMode.Mean =>
                    Math.Round(levels.Where(x => x != 0).Mean()),
                RacesSettings.AverageMode.GeometricMean =>
                    Math.Round(levels.Where(x => x != 0).GeometricMean()),
                RacesSettings.AverageMode.HarmonicMean =>
                    Math.Round(levels.Where(x => x != 0).HarmonicMean()),
                RacesSettings.AverageMode.RootMeanSquare =>
                    Math.Round(levels.Where(x => x != 0).RootMeanSquare()),
                _ =>
                    Math.Round(levels.Where(x => x != 0).Median()),
            };
            return (!double.IsNormal(ret)) ? 0 : ret;
        }

        static bool RaceGroupLookup(Dictionary<string, string[]> racesGroups, string? race, out string? key, out string? val)
        {
            key = null;
            val = null;
            if (race == null) return false;
            foreach (var raceGroup in racesGroups)
            {
                foreach (var sfx in raceGroup.Value)
                {
                    if (race.StartsWith(sfx, StringComparison.InvariantCultureIgnoreCase))
                    {
                        key = raceGroup.Key;
                        val = race[sfx.Length..];
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
