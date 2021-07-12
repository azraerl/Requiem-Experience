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
    class QuestPatcher
    {
        public static bool RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, Settings Settings)
        {
            Console.WriteLine($@"Settings.QuestSettings.PatchQuests is {Settings.QuestSettings.PatchQuests}");
            if (!Settings.QuestSettings.PatchQuests)
            {
                return false;
            }

            var questConfig = RequiemExperience.Properties.Resources.quests.Split('\n', StringSplitOptions.TrimEntries)
                .Where(x => !x.StartsWith('#') && x.Length != 0)
                .Select(x => x.Split(new char[] { ';' })[0].Trim())
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

            StringBuilder? quests = Settings.QuestSettings.Debug ? new StringBuilder() : null;
            quests?.Append("EditorID;Type;Name;Stages;Stages Text\r\n");

            Console.WriteLine($"Processing Quests Patch:\r\n + Overrides count is {questOverride.Count}\r\n + Conditions count is {questCond.Count}\r\n + Debug = {quests != null}");

            FormList? radiantExcl = null;
            if (questCond.Count > 0)
            {
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
            if (quests != null)
            {
                var outputPath = $@"{state.DataFolderPath}\SKSE\Plugins\Experience\";
                Console.WriteLine($@"Writing debug file: {outputPath}quests.csv");
                File.WriteAllText($@"{outputPath}quests.csv", quests?.ToString());
            }
            return anyQuests;
        }
    }
}
