using MathNet.Numerics.Statistics;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RequiemExperience
{
    [SupportedOSPlatform("windows7.0")]
    [SupportedOSPlatform("windows10")]
    class RacePatcher
    {
        public static bool RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, Settings Settings)
        {
            bool any = false;
            Console.WriteLine($@"Settings.RaceSettings.PatchRaces is {Settings.RaceSettings.PatchRaces}");
            if (!Settings.RaceSettings.PatchRaces)
            {
                return any;
            }

            var racesConfig = RequiemExperience.Properties.Resources.npcs.Split('\n', StringSplitOptions.TrimEntries)
                 .Where(x => !x.StartsWith('#') && x.Length != 0)
                 .Select(x => x.Split(new char[] { ';' })[0].Trim())
                 .Select(x => x.Split(new char[] { '=', ':' }, StringSplitOptions.RemoveEmptyEntries));

            StringBuilder? npcs = Settings.RaceSettings.Debug ? new StringBuilder() : null;
            npcs?.Append("NPC;Race;Level;Comments\r\n");

            var averageMode = racesConfig.Where(x => x.Length >= 2 && x[0].Equals("mode", StringComparison.InvariantCultureIgnoreCase))
                .Select(x => x[1].Trim().ToLowerInvariant()).DefaultIfEmpty("median").First();
            var raceOutFile = racesConfig.Where(x => x.Length >= 2 && x[0].Equals("file", StringComparison.InvariantCultureIgnoreCase))
                .Select(x => x[1].Trim().ToLowerInvariant()).DefaultIfEmpty("DefaultRaces").First();
            var racesGroups = racesConfig
                .Where(x => x.Length >= 3 && x[0].Equals("Groups", StringComparison.InvariantCultureIgnoreCase))
                .ToDictionary(
                    x => x[1].Trim(),
                    x => x[2].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries)
                );
            var ignoreableNPCs = racesConfig
                .Where(x => x.Length >= 2 && x[0].Equals("Ignore", StringComparison.InvariantCultureIgnoreCase))
                .Select(x => x[1].Trim()).ToList();
            var uniqueNPCs = racesConfig
                .Where(x => x.Length >= 2 && x[0].Equals("Unique", StringComparison.InvariantCultureIgnoreCase))
                .Select(x => x[1].Trim()).ToList();

            var overrideRaces = racesConfig
                .Where(x => x.Length >= 2 && x[0].Equals("Override", StringComparison.InvariantCultureIgnoreCase))
                .ToDictionary(
                    x => x[1].Trim(),
                    x => int.TryParse(x[2].Trim(), out var res) ? res : 0
                );
            Expressive.Expression exp = new Expressive.Expression(Settings.RaceSettings.LevelFormula, Expressive.ExpressiveOptions.IgnoreCaseAll);

            Console.WriteLine($"Processing NPC Races:\r\n + Averaging mode is {averageMode}\r\n + Groups count is {racesGroups.Count}\r\n + Ignorable NPCs count is {ignoreableNPCs.Count}\r\n + Unique NPCs count is {uniqueNPCs.Count}\r\n + Race XP overrides count is {overrideRaces.Count}\r\n + Level Expression is {Settings.RaceSettings.LevelFormula}\r\n + Debug = {npcs != null}");

            var races = new StringBuilder();
            var racesLevels = new Dictionary<string, ICollection<double>>();
            foreach (var npc in state.LoadOrder.PriorityOrder.WinningOverrides<INpcGetter>())
            {
                int level = -1;
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
                var ignore = npc.EditorID == null
                    || ignoreableNPCs.Where(x => Regex.IsMatch(npc.EditorID, "^" + x + "$", RegexOptions.IgnoreCase)).Any();
                var unique = npc.EditorID == null
                    || uniqueNPCs.Where(x => Regex.IsMatch(npc.EditorID, "^" + x + "$", RegexOptions.IgnoreCase)).Any();
                //npc.AttackRace.TryResolve(state.LinkCache, out arace);
                if (npc.Race.TryResolve(state.LinkCache, out var race) && !ignore)
                {
                    EditorID = race.EditorID;
                    if (unique)
                    {
                        var otherFormLists = state.LoadOrder.PriorityOrder.WinningOverrides<IFormListGetter>()
                            .Where(x => x.ContainedFormLinks.Any(y => race.FormKey == y.FormKey))
                            .Select(x => state.PatchMod.FormLists.GetOrAddAsOverride(x));

                        EditorID = EditorID + "__" + npc.EditorID;
                        var newRace = state.PatchMod.Races.AddNew(EditorID);
                        newRace.DeepCopyIn(race, out var errorMask, new Race.TranslationMask(defaultOn: true)
                        {
                            EditorID = false
                        });
                        newRace.MorphRace.SetTo(!race.MorphRace.IsNull ? race.MorphRace : race.AsNullableLink());
                        newRace.AttackRace.SetTo(!race.AttackRace.IsNull ? race.AttackRace : race.AsNullableLink());
                        newRace.ArmorRace.SetTo(!race.ArmorRace.IsNull ? race.ArmorRace : race.AsNullableLink());
                        var patchN = state.PatchMod.Npcs.GetOrAddAsOverride(npc);
                        patchN.Race.SetTo(newRace);
                        foreach (var otherFormList in otherFormLists)
                        {
                            var formLinks = otherFormList.ContainedFormLinks;
                            if (formLinks.Any(x => x.FormKey == race.FormKey))
                            {
                                otherFormList.Items.Add(newRace);
                            }
                        }
                        any = true;
                    }
                    else
                    {
                        raceGroupLookup(racesGroups, EditorID, out key, out val);
                    }

                    if (key != null && racesGroups.TryGetValue(key, out var group))
                    {
                        if (!racesLevels.TryGetValue(key + val, out var levels))
                        {
                            racesLevels.Add(key + val, new List<double>() { level });
                        }
                        else
                        {
                            levels.Add(level);
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
                npcs?.Append(npc.EditorID + "," + EditorID + "," + level + ","
                    + key + val + (ignore ? " ignored" : "") + (unique ? " unique" : "") + "\r\n");
            }

            foreach (var race in state.LoadOrder.PriorityOrder.WinningOverrides<IRaceGetter>())
            {
                raceGroupLookup(racesGroups, race.EditorID, out var key, out var val);
                if (overrideRaces.TryGetValue(race.EditorID ?? "null", out int overrid))
                {
                    races.Append(race.EditorID);
                    races.Append(",");
                    races.Append(express(overrid, exp));
                    races.Append('\n');
                }
                else if (key != null && racesLevels.TryGetValue(key + val, out var glevels) && glevels.Any())
                {
                    races.Append(race.EditorID);
                    races.Append(",");
                    races.Append(express(average(glevels, averageMode), exp));
                    races.Append('\n');
                }
                else if (racesLevels.TryGetValue(race.EditorID ?? "null", out var levels) && levels.Any())
                {
                    races.Append(race.EditorID);
                    races.Append(",");
                    races.Append(express(average(levels, averageMode), exp));
                    races.Append('\n');
                }
                else if (race.Starting.TryGetValue(BasicStat.Health, out var startingHealth))
                {
                    // fallback for races which don't have NPCs defined
                    races.Append(race.EditorID);
                    races.Append(",");
                    races.Append(express((int)Math.Floor(Math.Sqrt(startingHealth) + 0.5), exp));
                    races.Append('\n');
                }
            }

            var outputPath = $@"{state.DataFolderPath}\SKSE\Plugins\Experience\";
            Console.WriteLine($@"Creating folder: {outputPath}Races\");
            Directory.CreateDirectory($@"{outputPath}Races\");
            Console.WriteLine($@"Writing races patch: {outputPath}Races\{raceOutFile}.csv");
            File.WriteAllText($@"{outputPath}Races\{raceOutFile}.csv", races.ToString());

            if (npcs != null)
            {
                Console.WriteLine($@"Writing debug file: {outputPath}npcs.csv");
                File.WriteAllText($@"{outputPath}npcs.csv", npcs?.ToString());
            }

            return any;
        }

        static int express( int level, Expressive.Expression ex )
        {
            if( ex == null )
            {
                return level;
            }
            var result = ex.Evaluate(new Dictionary<string, object> { ["level"] = level });
            if( result is double )
            {
                level = Convert.ToInt32(Math.Ceiling((double)result));
            } else
            {
                level = Convert.ToInt32(result);
            }
            return level;
        }

        static int average(ICollection<double> levels, string mode)
        {
            double ret = double.NaN;
            switch (mode)
            {
                case "mean":
                case "average":
                    ret = Math.Round(levels.Where(x => x != 0).Mean());
                    break;
                case "geometric":
                    ret = Math.Round(levels.Where(x => x != 0).GeometricMean());
                    break;
                case "harmonic":
                    ret = Math.Round(levels.Where(x => x != 0).HarmonicMean());
                    break;
                case "rms":
                    ret = Math.Round(levels.Where(x => x != 0).RootMeanSquare());
                    break;
                case "median":
                default:
                    ret = Math.Round(levels.Where(x => x != 0).Median());
                    break;
            }
            return (!double.IsNormal(ret)) ? 0 : (int)ret;
        }

        static void raceGroupLookup(Dictionary<string, string[]> racesGroups, string? race, out string? key, out string? val)
        {
            key = null;
            val = null;
            if (race == null) return;
            foreach (var raceGroup in racesGroups)
            {
                foreach (var sfx in raceGroup.Value)
                {
                    if (race.StartsWith(sfx, StringComparison.InvariantCultureIgnoreCase))
                    {
                        key = raceGroup.Key;
                        val = race.Substring(sfx.Length);
                        break;
                    }
                }
                if (key != null)
                {
                    break;
                }
            }
        }
    }
}
