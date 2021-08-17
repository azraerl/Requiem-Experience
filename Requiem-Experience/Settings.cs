using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RequiemExperience
{
    public class Settings
    {
        [SynthesisSettingName("General")]
        public General General = new General();
        [SynthesisSettingName("Skills-related Settings")]
        public SkillSettings SkillSettings = new SkillSettings();
        [SynthesisSettingName("Killing XP settings")]
        public RacesSettings RacesSettings = new RacesSettings();
        [SynthesisSettingName("Quests XP settings")]
        public QuestSettings QuestSettings = new QuestSettings();
    }
    public class General
    {
        [SynthesisSettingName("Preset for Experience")]
        [SynthesisTooltip("Apply (overwrite if exists) Experience.ini with preset\r\nNone - do not overwrite\r\nTrue Unlevelled - Killing XP values do not change\r\nExtra Rewarding - Killing XP values the bigger the higher enemies you hunt")]
        public ExperiencePreset Preset = ExperiencePreset.TrueUnlevelled;

        [SynthesisSettingName("Enable debug")]
        [SynthesisTooltip("Generate additional debug files")]
        public bool Debug = false;

        public enum ExperiencePreset
        {
            None, TrueUnlevelled, ExtraRewarding
        }
    }

    public class SkillSettings
    {
        [SynthesisTooltip("This suppresses Skill XP gain from use \n" +
            "(e.g. vanilla skill leveling). \n" +
            "This is meant to be used along with R-SSL mod; does nothing for 3-B-FTweaks")]
        public bool SuppressSkillGains = true;

        [SynthesisTooltip("Remove Skills from Skill Books")]
        public bool PatchSkillBooks = true;

        [SynthesisTooltip("Skill Books value multiplier, %. Requires Skill Books patching to be enabled.")]
        public uint SkillBooksValueMultiplier = 200;
    }

    public class RacesSettings
    {
        [SynthesisSettingName("Patch Killing XP")]
        [SynthesisTooltip("Do racial tweaks as per below - no race/kill-xp changes will be done if this is disabled.")]
        public bool PatchRaces = true;

        [SynthesisTooltip("Output file, default value means to overwrite the one which comes with Experience mod")]
        public string OutputFile = "DefaultRaces";

        [SynthesisSettingName("Averaging Function")]
        [SynthesisTooltip("Function to average race level basing on its members")]
        public AverageMode Mode = AverageMode.Mean;

        [SynthesisTooltip("Formula which converts enemy level into XP value. Old formula is just \"[level]\"")]
        public string LevelFormula = "pow( [level], 1.28 ) / 2";

        [SynthesisSettingName("NPCs to ignore")]
        [SynthesisTooltip("List of NPC EditorIDs to be ignored from averaging. Accepts regular expressions.")]
        public List<NPC> Ignore = new List<NPC>();

        [SynthesisSettingName("Unique NPCs")]
        [SynthesisTooltip("Unique NPCs also being ignored from averaging. However additionally their races will be duplicated, so custom XP can be applied\r\nNew Race EditorID is generated as \" < race EditorID > __ < npc EditorID > \"")]
        public List<NPC> Unique = new List<NPC>();

        [SynthesisSettingName("Overridden Races")]
        [SynthesisTooltip("Overrides for particular race EditorIDs")]
        public List<RaceOverride> Override = new List<RaceOverride>();

        [SynthesisSettingName("Grouped Races")]
        [SynthesisTooltip("Race prefixes which must be grouped up to avoid racism, e.g. XP will be even")]
        public List<RaceGroup> Grouping = new List<RaceGroup>();

        public RacesSettings()
        {
            var racesCfg = RequiemExperience.Properties.Resources.npcs
                    .Split('\n', StringSplitOptions.TrimEntries)
                    .Where(x => !x.StartsWith('#') && x.Length != 0)
                    .Select(x => x.Split(new char[] { ';' })[0].Trim())
                    .Select(x => x.Split(new char[] { '=', ':' }, StringSplitOptions.RemoveEmptyEntries));

            Ignore.AddRange(
                racesCfg
                    .Where(x => x.Length >= 2 && x[0].Equals("Ignore", StringComparison.InvariantCultureIgnoreCase))
                    .Select(x => new NPC(x[1].Trim())).ToList()
            );
            Unique.AddRange(
                racesCfg
                    .Where(x => x.Length >= 2 && x[0].Equals("Unique", StringComparison.InvariantCultureIgnoreCase))
                    .Select(x => new NPC(x[1].Trim())).ToList()
            );
            Override.AddRange(
                racesCfg
                    .Where(x => x.Length >= 2 && x[0].Equals("Override", StringComparison.InvariantCultureIgnoreCase))
                    .ToDictionary(
                        x => x[1].Trim(),
                        x => int.TryParse(x[2].Trim(), out var res) ? res : 0
                    ).Select(
                        x => new RaceOverride(x.Key,x.Value)
                    )
            );
            Grouping.AddRange(
                racesCfg
                    .Where(x => x.Length >= 3 && x[0].Equals("Groups", StringComparison.InvariantCultureIgnoreCase))
                    .ToDictionary(
                        x => x[1].Trim(),
                        x => x[2].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries)
                    ).Select(
                        x => new RaceGroup(x.Key,new List<string>( x.Value ))
                    )
            );
        }

        public enum AverageMode
        {
            Mean, GeometricMean, HarmonicMean, RootMeanSquare, Median
        }

        public class NPC
        {
            [SynthesisSettingName("NPC Editor ID")]
            public string name;

            [SynthesisIgnoreSetting]
            private Regex? regex = null;
            public NPC(string name)
            {
                this.name = name;
            }
            public Regex asRegex()
            {
                if (regex == null)
                {
                    regex = new Regex("^" + this.name + "$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);
                }
                return regex;
            }
        }
        public class RaceGroup
        {
            [SynthesisSettingName("Group name")]
            [SynthesisTooltip("Logical name, does not affect anything")]
            public string name;

            [SynthesisSettingName("Race prefixes")]
            public List<string> value;
            public RaceGroup(string name, List<string> value)
            {
                this.name = name;
                this.value = value;
            }
        }
        public class RaceOverride
        {
            [SynthesisSettingName("Race Editor ID")]
            public string name;

            [SynthesisSettingName("XP Value")]
            [SynthesisTooltip("Note that Level formula will be applied to this value")]
            public int value;
            public RaceOverride(string name, int value)
            {
                this.name = name;
                this.value = value;
            }
        }
    }

    public class QuestSettings
    {
        [SynthesisSettingName("Patch Quests XP")]
        [SynthesisTooltip("Do Quests patching as per below - no race/kill-xp changes will be done if this is disabled.")]
        public bool PatchQuests = true;

        [SynthesisSettingName("QuestType overrides")]
        [SynthesisTooltip("Set of rules how to patch quests\r\nGeneral format: Quest Name (or regex) = Quest Type to set\r\nQuest Types are: Misc, None, SideQuest, CompanionQuest, MainQuest, Vampire")]
        public List<QuestRule> QuestRules = new List<QuestRule>();

        [SynthesisIgnoreSetting]
        public List<QuestCondition> QuestConditions = new List<QuestCondition>();

        public QuestSettings()
        {
            QuestRules.AddRange(
                RequiemExperience.Properties.Resources.quests
                    .Split('\n', StringSplitOptions.TrimEntries)
                    .Where(x => !x.StartsWith('#') && x.Length != 0)
                    .Select(x => x.Split(new char[] { ';' })[0].Trim().Split(new char[] { '=', ':' }, StringSplitOptions.RemoveEmptyEntries))
                    .Where(x => x.Length >= 2)
                    .Select(
                        x => new QuestRule(
                            x[0].Trim(),
                            Enum.TryParse<QuestRule.QuestType>(x[1].Trim(), true, out var res) ? res : QuestRule.QuestType.Misc
                        )
                    )
            );
        }

        public class QuestRule
        {
            [SynthesisSettingName("Quest Editor ID")]
            [SynthesisTooltip("Quest Editor ID. Also accepts regular expressions.")]
            public string name;

            [SynthesisSettingName("Quest Type")]
            public QuestType value;

            [SynthesisIgnoreSetting]
            private Regex? regex = null;
            public QuestRule(string name, QuestType value)
            {
                this.name = name;
                this.value = value;
            }
            public Regex asRegex()
            {
                if (regex == null)
                {
                    regex = new Regex("^" + this.name + "$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);
                }
                return regex;
            }
            public Quest.TypeEnum asType()
            {
                return (Quest.TypeEnum)value;
            }

            public enum QuestType
            {
                Misc = 6,
                None = 0,
                MainQuest = 1,
                MageGuild = 2,
                ThievesGuild = 3,
                DarkBrotherhood = 4,
                CompanionQuests = 5,
                Daedric = 7,
                SideQuest = 8,
                CivilWar = 9,
                Vampire = 10,
                Dragonborn = 11
            }
        }

        public class QuestCondition
        {
            [SynthesisSettingName("Quest Editor ID")]
            [SynthesisTooltip("Quest Editor ID. Also accepts regular expressions.")]
            public string name;

            [SynthesisSettingName("Alias name")]
            public string value;
            public QuestCondition(string name, string value)
            {
                this.name = name;
                this.value = value;
            }
        }
    }
}
