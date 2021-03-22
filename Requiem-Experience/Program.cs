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

namespace RequiemExperience
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .SetTypicalOpen(GameRelease.SkyrimSE, "Experience_QuestsPatch.esp")
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .Run(args);
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
            var outputPath = $@"{state.DataFolderPath}\SKSE\Plugins\Experience\";
            var questConfig = RequiemExperience.Properties.Resources.quests.Split('\n', StringSplitOptions.TrimEntries)
                .Where(x => !x.StartsWith('#') && x.Length != 0)
                .Select(x => x.Split(new char[] {';'})[0].Trim())
                .Select(x => x.Split(new char[] { '=', ':' }, StringSplitOptions.RemoveEmptyEntries));
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

            Console.WriteLine($"Processing Quests Patch:\r\n + Overrides count is {questOverride.Count}\r\n + Conditions count is {questCond.Count}\r\n + Debug = {quests != null}");

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
                            func.Function = Condition.Function.GetInCurrentLocFormList;
                            func.ParameterOneRecord.SetTo(radiantExcl);
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
                .Select(x => x.Split(new char[] { ';' })[0].Trim())
                .Select(x => x.Split(new char[] { '=', ':' }, StringSplitOptions.RemoveEmptyEntries));

            StringBuilder? npcs = null;
            if (racesConfig.Where(x => x.Length >= 1 && x[0].Equals("debug", StringComparison.InvariantCultureIgnoreCase)).Any())
            {
                npcs = new StringBuilder();
                npcs?.Append("NPC;Race;Level;Comments\r\n");
            }
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

            Console.WriteLine($"Processing NPC Races:\r\n + Averaging mode is {averageMode}\r\n + Groups count is {racesGroups.Count}\r\n + Ignorable NPCs count is {ignoreableNPCs.Count}\r\n + Unique NPCs count is {uniqueNPCs.Count}\r\n + Race XP overrides count is {overrideRaces.Count}\r\n + Debug = {npcs != null}");

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
                        newRace.DeepCopyIn(race, new Race.TranslationMask(defaultOn: true)
                        {
                            EditorID = false
                        });
                        newRace.MorphRace.SetTo(!race.MorphRace.IsNull ? race.MorphRace : race.AsNullableLink());
                        newRace.AttackRace.SetTo(!race.AttackRace.IsNull ? race.AttackRace : race.AsNullableLink());
                        newRace.ArmorRace.SetTo(!race.AttackRace.IsNull ? race.AttackRace : race.AsNullableLink());
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
            Console.WriteLine($@"Writing races patch: {outputPath}Races\{raceOutFile}.csv");
            File.WriteAllText($@"{outputPath}Races\{raceOutFile}.csv", races.ToString());
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
