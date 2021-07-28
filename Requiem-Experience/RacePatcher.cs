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
            Console.WriteLine($@"Settings.RaceSettings.PatchRaces is {Settings.RacesSettings.PatchRaces}");
            if (!Settings.RacesSettings.PatchRaces)
            {
                return any;
            }

            StringBuilder? npcs = Settings.General.Debug ? new StringBuilder(50*1024) : null;
            npcs?.Append("NPC;Race;Level;Comments\r\n");

            Expressive.Expression exp = new Expressive.Expression(Settings.RacesSettings.LevelFormula, Expressive.ExpressiveOptions.IgnoreCaseAll);

            Console.WriteLine($"Processing NPC Races:\r\n + Averaging mode is {Settings.RacesSettings.Mode}\r\n + Groups count is {Settings.RacesSettings.Grouping.Count}\r\n + Ignorable NPCs count is {Settings.RacesSettings.Ignore.Count}\r\n + Unique NPCs count is {Settings.RacesSettings.Unique.Count}\r\n + Race XP overrides count is {Settings.RacesSettings.Override.Count}\r\n + Level Expression is {Settings.RacesSettings.LevelFormula}\r\n + Debug = {npcs != null}");

            var races = new StringBuilder(10*1024); // 10 KiB
            var racesLevels = new Dictionary<string, ICollection<double>>();
            var racesOverrs = Settings.RacesSettings.Override.ToDictionary(x => x.name, x => x.value);
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
                    || Settings.RacesSettings.Ignore.Any(x => x.asRegex().IsMatch(npc.EditorID))
                    || Settings.RacesSettings.Override.Any(x => x.name.Equals(npc.EditorID, StringComparison.OrdinalIgnoreCase));
                var unique = npc.EditorID != null
                    && Settings.RacesSettings.Unique.Any(x => x.asRegex().IsMatch(npc.EditorID));
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

                    if(!unique && raceGroupLookup(Settings.RacesSettings.Grouping, EditorID, out key, out val))
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
                npcs?.Append(npc.EditorID + "," + EditorID + "," + level + ","
                    + key + val + (ignore ? " ignored" : "") + (unique ? " unique" : "") + "\r\n");
            }

            foreach (var race in state.LoadOrder.PriorityOrder.WinningOverrides<IRaceGetter>())
            {
                if (racesOverrs.TryGetValue(race.EditorID ?? "null", out int overrid))
                {
                    races.Append(race.EditorID);
                    races.Append(",");
                    races.Append(express(overrid, exp));
                    races.Append('\n');
                }
                else if (raceGroupLookup(Settings.RacesSettings.Grouping, race.EditorID, out var key, out var val)
                    && racesLevels.TryGetValue(key + val, out var glevels) && glevels.Any())
                {
                    races.Append(race.EditorID);
                    races.Append(",");
                    races.Append(express(average(glevels, Settings.RacesSettings.Mode), exp));
                    races.Append('\n');
                }
                else if (racesLevels.TryGetValue(race.EditorID ?? "null", out var levels) && levels.Any())
                {
                    races.Append(race.EditorID);
                    races.Append(",");
                    races.Append(express(average(levels, Settings.RacesSettings.Mode), exp));
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
            Console.WriteLine($@"Writing races patch: {outputPath}Races\{Settings.RacesSettings.OutputFile}.csv");
            File.WriteAllText($@"{outputPath}Races\{Settings.RacesSettings.OutputFile}.csv", races.ToString());

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

        static int average(ICollection<double> levels, RacesSettings.AverageMode mode)
        {
            double ret = double.NaN;
            switch (mode)
            {
                case RacesSettings.AverageMode.Mean:
                    ret = Math.Round(levels.Where(x => x != 0).Mean());
                    break;
                case RacesSettings.AverageMode.GeometricMean:
                    ret = Math.Round(levels.Where(x => x != 0).GeometricMean());
                    break;
                case RacesSettings.AverageMode.HarmonicMean:
                    ret = Math.Round(levels.Where(x => x != 0).HarmonicMean());
                    break;
                case RacesSettings.AverageMode.RootMeanSquare:
                    ret = Math.Round(levels.Where(x => x != 0).RootMeanSquare());
                    break;
                case RacesSettings.AverageMode.Median:
                default:
                    ret = Math.Round(levels.Where(x => x != 0).Median());
                    break;
            }
            return (!double.IsNormal(ret)) ? 0 : (int)ret;
        }

        static bool raceGroupLookup(List<RacesSettings.RaceGroup> racesGroups, string? race, out string? key, out string? val)
        {
            key = null;
            val = null;
            if (race == null) return false;
            foreach (var raceGroup in racesGroups)
            {
                foreach (var sfx in raceGroup.value)
                {
                    if (race.StartsWith(sfx, StringComparison.InvariantCultureIgnoreCase))
                    {
                        key = raceGroup.name;
                        val = race[sfx.Length..];
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
