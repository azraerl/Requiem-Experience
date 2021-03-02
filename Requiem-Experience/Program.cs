using MathNet.Numerics.Statistics;
using System;
using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("Windows7.0")]
namespace RequiemExperience
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .Run(args, new RunPreferences()
                {
                    ActionsForEmptyArgs = new RunDefaultPatcher()
                    {
                        IdentifyingModKey = "Experience_QuestsPatch.esp",
                        TargetRelease = GameRelease.SkyrimSE,
                        BlockAutomaticExit = true,
                    }
                });
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

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var outputPath = $@"{state.Settings.DataFolderPath}\SKSE\Plugins\Experience\";
            var questConfig = RequiemExperience.Properties.Resources.quests.Split('\n', StringSplitOptions.TrimEntries)
                .Where(x => !x.StartsWith('#') && x.Length != 0)
                .Select(x => x.Split(new char[] { '=', ';', ':' }, StringSplitOptions.RemoveEmptyEntries));
            var questOverride = questConfig
                .Where(x => x.Length >= 2)
                .ToDictionary(
                    x => x[0].Trim(),
                    x => Enum.TryParse<Quest.TypeEnum>(x[1].Trim(), true, out var res) ? res : Quest.TypeEnum.Misc
                );
            var questCond = questConfig
                .Where(x => x.Length >= 3)
                .ToDictionary(
                    x => x[0].Trim(),
                    x => x[2].Trim()
                );
            StringBuilder? quests = null;
            if (questConfig.Where(x => x.Length >= 1 && x[0].Equals("debug", StringComparison.InvariantCultureIgnoreCase)).Any())
            {
                quests = new StringBuilder();
            }
            quests?.Append("EditorID;Type;Name;Stages;Stages Text\r\n");

            Console.WriteLine($@"Processing Quests Patch: debug is {quests != null}, overrides count is {questOverride.Count}, conditions count is {questCond.Count}.");

            FormList? radiantExcl = null;
            if ( questCond.Count > 0 ) { 
                radiantExcl = state.PatchMod.FormLists.AddNew("vf_RadiantExclusion");
                radiantExcl.FormVersion = 44;
            }

            bool anyQuests = false;
            foreach (var quest in state.LoadOrder.PriorityOrder.WinningOverrides<IQuestGetter>())
            {
                if (quest.EditorID == null) continue;

                string? key = null;
                Quest? patchQ = null;
                if (questOverride.TryGetValue(quest.EditorID, out var type))
                {
                    key = quest.EditorID;
                    patchQ = state.PatchMod.Quests.GetOrAddAsOverride(quest);
                    patchQ.Type = type;
                    anyQuests = true;
                }
                else
                {
                    var lookup = questOverride
                        .Where(d => Regex.IsMatch(quest.EditorID, "^" + d.Key + "$", RegexOptions.IgnoreCase))
                        .ToDictionary(d => d.Key, d => d.Value);
                    if (lookup.Values.Count == 1)
                    {
                        key = lookup.Keys.ElementAt(0);
                        patchQ = state.PatchMod.Quests.GetOrAddAsOverride(quest);
                        patchQ.Type = lookup.Values.ElementAt(0);
                        anyQuests = true;
                    }
                }

                if (key != null && patchQ != null && radiantExcl != null && questCond.TryGetValue(key, out var condition))
                {
                    foreach (var alias in patchQ.Aliases)
                    {
                        if (alias.Name != null && alias.Name.Equals(condition, StringComparison.InvariantCultureIgnoreCase))
                        {
                            ConditionFloat cond = new ConditionFloat();
                            cond.CompareOperator = CompareOperator.NotEqualTo;
                            cond.ComparisonValue = 1.0f;
                            FunctionConditionData func = new FunctionConditionData();
                            func.Function = (ushort)ConditionData.Function.GetInCurrentLocFormList;
                            func.ParameterOneRecord = radiantExcl;
                            cond.Data = func;
                            alias.Conditions.Insert(1, cond);
                        }
                    }
                }

                if (quests != null)
                {
                    quests?.Append(quest.EditorID + ";" + quest.Type + ";\"" + (quest.Name?.ToString() ?? "null").Replace('"', '-') + "\";" + quest.Objectives.Count + ";\"");
                    foreach (var obj in quest.Objectives)
                    {
                        quests?.Append((obj.DisplayText?.ToString() ?? "null").Replace('"', '-') + ", ");
                    }
                    quests?.Append("\"\r\n");
                }
            }

            var racesConfig = RequiemExperience.Properties.Resources.npcs.Split('\n', StringSplitOptions.TrimEntries)
                .Where(x => !x.StartsWith('#') && x.Length != 0)
                .Select(x => x.Split(new char[] { '=', ';', ':' }, StringSplitOptions.RemoveEmptyEntries));

            StringBuilder? npcs = null;
            if (racesConfig.Where(x => x.Length >= 1 && x[0].Equals("debug", StringComparison.InvariantCultureIgnoreCase)).Any())
            {
                npcs = new StringBuilder();
                npcs?.Append("NPC;Race;AtkRace;Level;Comments\r\n");
            }
            var averageMode = racesConfig.Where(x => x.Length >= 2 && x[0].Equals("mode", StringComparison.InvariantCultureIgnoreCase))
                .Select(x => x[1].Trim().ToLowerInvariant()).DefaultIfEmpty("median").First();
            var racesGroups = racesConfig
                .Where(x => x.Length >= 3 && x[0].Equals("Groups", StringComparison.InvariantCultureIgnoreCase))
                .ToDictionary(
                    x => x[1].Trim(),
                    x => x[2].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries)
                );
            var ignoreableNPCs = racesConfig
                .Where(x => x.Length >= 2 && x[0].Equals("Ignore", StringComparison.InvariantCultureIgnoreCase))
                .Select(x => x[1].Trim()).ToList();
            var overrideRaces = racesConfig
                .Where(x => x.Length >= 2 && x[0].Equals("Override", StringComparison.InvariantCultureIgnoreCase))
                .ToDictionary(
                    x => x[1].Trim(),
                    x => int.TryParse(x[2].Trim(), out var res) ? res : 0
                );

            Console.WriteLine($@"Processing NPC Races: averaging more is {averageMode}, debug is {npcs != null}, groups count is {racesGroups.Count}, ignorable NPCs count is {ignoreableNPCs.Count}, overrides count is {overrideRaces.Count}.");

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
                IRaceGetter? race = null, arace = null;
                var ignore = npc.EditorID == null
                    || ignoreableNPCs.Where(x => Regex.IsMatch(npc.EditorID, "^" + x + "$", RegexOptions.IgnoreCase)).Any();
                npc.AttackRace.TryResolve(state.LinkCache, out arace);
                if (npc.Race.TryResolve(state.LinkCache, out race) && !ignore)
                {
                    raceGroupLookup(racesGroups, race.EditorID, out key, out val);
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
                    else if (race.EditorID != null && racesLevels.TryGetValue(race.EditorID, out var levels))
                    {
                        levels.Add(level);
                    }
                    else if (race.EditorID != null)
                    {
                        racesLevels.Add(race.EditorID, new List<double>() { level });
                    }
                }
                npcs?.Append(npc.EditorID + "," + race?.EditorID + "," + arace?.EditorID + ","
                    + level + "," + key + val + (ignore ? "ignored" : "") + "\r\n");
            }

            foreach (var race in state.LoadOrder.PriorityOrder.WinningOverrides<IRaceGetter>())
            {
                raceGroupLookup(racesGroups, race.EditorID, out var key, out var val);
                if (overrideRaces.TryGetValue(race.EditorID ?? "null", out int overrid))
                {
                    races.Append(race.EditorID);
                    races.Append(",");
                    races.Append(overrid);
                    races.Append('\n');
                }
                else if (key != null && racesLevels.TryGetValue(key + val, out var glevels) && glevels.Any())
                {
                    races.Append(race.EditorID);
                    races.Append(",");
                    races.Append(average(glevels, averageMode));
                    races.Append('\n');
                }
                else if (racesLevels.TryGetValue(race.EditorID ?? "null", out var levels) && levels.Any())
                {
                    races.Append(race.EditorID);
                    races.Append(",");
                    races.Append(average(levels, averageMode));
                    races.Append('\n');
                }
                else if (race.Starting.TryGetValue(BasicStat.Health, out var startingHealth))
                {
                    // fallback for races which don't have NPCs defined
                    races.Append(race.EditorID);
                    races.Append(",");
                    races.Append(Math.Floor(Math.Sqrt(startingHealth) + 0.5));
                    races.Append('\n');
                }
            }

            Console.WriteLine($@"Creating folder: {outputPath}");
            Directory.CreateDirectory(outputPath);
            Console.WriteLine($@"Writing races patch: {outputPath}Races\Requiem.csv");
            File.WriteAllText($@"{outputPath}Races\Requiem.csv", races.ToString());
            if (quests != null)
            {
                Console.WriteLine($@"Writing debug file: {outputPath}quests.csv");
                File.WriteAllText($@"{outputPath}quests.csv", quests?.ToString());
            }
            if (npcs != null)
            {
                Console.WriteLine($@"Writing debug file: {outputPath}npcs.csv");
                File.WriteAllText($@"{outputPath}npcs.csv", npcs?.ToString());
            }
            if(!anyQuests)
            {
                state.PatchMod.Clear();
            }
        }
    }
}
