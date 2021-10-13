using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Newtonsoft.Json.Linq;
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
            Console.WriteLine($@"Settings.QuestSettings.PatchQuests is {Settings.General.PatchQuests}");
            if (!Settings.General.PatchQuests)
            {
                return false;
            }

            string settingsFile = state.ExtraSettingsDataPath + @"\QuestSettings.json";
            var questOverride = new Dictionary<Regex, Quest.TypeEnum>();
            var questCond = new Dictionary<string, string>();
            if (!File.Exists(settingsFile))
            {
                Console.WriteLine("\"QuestSettings.json\" not located in Users Data folder.");
            }
            else
            {
                var settingJson = JObject.Parse(File.ReadAllText(settingsFile));
                questCond = settingJson["Condition"]?.ToObject<Dictionary<string, string>>() ?? questCond;
                var ov = settingJson["Override"]?.ToObject<Dictionary<string, string>>();
                if ( ov != null)
                {
                    foreach (var qo in ov)
                    {
                        questOverride.Add(
                            new Regex("^" + qo.Key + "$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline),
                            (Quest.TypeEnum)Enum.Parse(typeof(Quest.TypeEnum), qo.Value)
                        );
                    }
                }
            }

            StringBuilder? quests = Settings.General.Debug ? new StringBuilder() : null;
            quests?.Append("FormID;EditorID;Type;Name;Stages;Stages Text\r\n");

            Console.WriteLine($"Processing Quests Patch:\r\n" +
                $" + Overrides count is {questOverride.Count}\r\n" +
                $" + Conditions count is {questCond.Count}\r\n" +
                $" + Debug = {quests != null}"
            );

            FormList ? radiantExcl = null;
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

                var lookup = questOverride
                    .Where(d => d.Key.IsMatch(quest.EditorID) )
                    .ToDictionary(d => d.Key.ToString(), d => d.Value);
                if (lookup.Count == 1)
                {
                    key = lookup.Keys.ElementAt(0);
                    patchQ = state.PatchMod.Quests.GetOrAddAsOverride(quest);
                    patchQ.Type = lookup.Values.ElementAt(0);
                    anyQuests = true;
                }
                else if (lookup.Count > 1)
                {
                    Console.WriteLine("WARN: Found more than one match for " + quest.EditorID);
                }

                if (key != null && patchQ != null)
                {
                    bool foundFail = false, foundCmpl = false;
                    foreach (var stage in patchQ.Stages)
                    {
                        foreach (var loge in stage.LogEntries)
                        {
                            foundFail |= loge.Flags.HasValue && loge.Flags.Value == QuestLogEntry.Flag.FailQuest;
                            foundCmpl |= loge.Flags.HasValue && loge.Flags.Value == QuestLogEntry.Flag.CompleteQuest;
                        }
                    }
                    if (!foundCmpl)
                    {
                        Console.WriteLine("WARN: No log entries flagged with CompleteQuest for " + quest.EditorID);
                    }
                    if (!foundFail)
                    {
                        Console.WriteLine("WARN: No log entries flagged with FailQuest for " + quest.EditorID);
                    }
                }

                if (key != null && patchQ != null && radiantExcl != null && questCond.TryGetValue(key, out var condition))
                {
                    foreach (var alias in patchQ.Aliases)
                    {
                        if (alias.Name != null && alias.Name.Equals(condition, StringComparison.InvariantCultureIgnoreCase))
                        {
                            ConditionFloat cond = new();
                            cond.CompareOperator = CompareOperator.NotEqualTo;
                            cond.ComparisonValue = 1.0f;
                            FunctionConditionData func = new();
                            func.Function = Condition.Function.GetInCurrentLocFormList;
                            func.ParameterOneRecord.SetTo(radiantExcl);
                            cond.Data = func;
                            alias.Conditions.Insert(1, cond);
                        }
                    }
                }

                if (quests != null)
                {
                    quests?.Append(
                        "[" + quest.FormKey.ModKey.FileName + "] XX" + quest.FormKey.IDString() + ";"
                        + quest.EditorID + ";" + quest.Type
                        + ";\"" + (quest.Name?.ToString() ?? "null").Replace('"', '-')
                        + "\";" + quest.Objectives.Count + ";\""
                    );
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
