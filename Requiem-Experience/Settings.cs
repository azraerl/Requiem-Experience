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
        public General General = new();
        [SynthesisSettingName("Skills-related Settings")]
        public SkillSettings SkillSettings = new();
        [SynthesisSettingName("Killing XP settings")]
        public RacesSettings RacesSettings = new();
    }
    public class General
    {
        [SynthesisSettingName("Patch Quests XP")]
        [SynthesisTooltip("Do Quests patching as per below - no race/kill-xp changes will be done if this is disabled.")]
        public bool PatchQuests = true;

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

        [SynthesisTooltip("Use Unique flag on NPC records if NPC level above threshold")]
        public int NPCUniqueFlagThreshold = 20;

        [SynthesisSettingName("Averaging Function")]
        [SynthesisTooltip("Function to average race level basing on its members")]
        public AverageMode Mode = AverageMode.Mean;

        [SynthesisTooltip("Formula which converts enemy level into XP value. Old formula is just \"[level]\"")]
        public string LevelFormula = "pow( [level], 1.28 ) / 2";

        public enum AverageMode
        {
            Mean, GeometricMean, HarmonicMean, RootMeanSquare, Median
        }
    }
}
