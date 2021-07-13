using Mutagen.Bethesda.Synthesis.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RequiemExperience
{
    public class Settings
    {
        public QuestSettings QuestSettings = new QuestSettings();
        public RaceSettings RaceSettings = new RaceSettings();
        public SkillSettings SkillSettings = new SkillSettings();
    }

    public class SkillSettings
    {
        [SynthesisTooltip("This suppresses Skill XP gain from use \n" +
            "(e.g. vanilla skill leveling). \n" +
            "This is meant to be used along with R-SSL mod; does nothing for 3-B-FTweaks")]
        public bool SuppressSkillGains = true;

        [SynthesisTooltip("Remove Skills from Skill Books")]
        public bool PatchSkillBooks = true;

        [SynthesisTooltip("Skill Books value multiplier, %")]
        public uint SkillBooksValueMultiplier = 150;
    }

    public class RaceSettings
    {
        [SynthesisTooltip("Do racial tweaks and generate DefaultRaces.csv")]
        public bool PatchRaces = true;

        [SynthesisTooltip("Formula which converts enemy level into XP value. Old formula is just \"[level]\"")]
        public string LevelFormula = "pow( [level], 1.35 ) / 2";

        [SynthesisTooltip("Generate debug file - npcs.csv")]
        public bool Debug = false;
    }

    public class QuestSettings
    {

        [SynthesisTooltip("Do Quests patching")]
        public bool PatchQuests = true;

        [SynthesisTooltip("Generate debug file - quests.csv")]
        public bool Debug = false;
    }
}
