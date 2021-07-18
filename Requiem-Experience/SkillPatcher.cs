using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace RequiemExperience
{
    [SupportedOSPlatform("windows7.0")]
    [SupportedOSPlatform("windows10")]
    class SkillPatcher
    {
        public static bool RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, Settings Settings)
        {
            bool any = false;
            Console.WriteLine($@"Settings.SkillSettings.SuppressSkillGains is {Settings.SkillSettings.SuppressSkillGains}");
            if (Settings.SkillSettings.SuppressSkillGains)
            {
                var list = new HashSet<string>();
                list.Add("AVAlteration");
                list.Add("AVConjuration");
                list.Add("AVDestruction");
                list.Add("AVEnchanting");
                list.Add("AVMysticism");
                list.Add("AVRestoration");
                list.Add("AVAlchemy");
                list.Add("AVLightArmor");
                list.Add("AVLockpicking");
                list.Add("AVPickpocket");
                list.Add("AVSneak");
                list.Add("AVSpeechcraft");
                list.Add("AVMarksman");
                list.Add("AVBlock");
                list.Add("AVHeavyArmor");
                list.Add("AVOneHanded");
                list.Add("AVSmithing");
                list.Add("AVTwoHanded");
                foreach (var avi in state.LoadOrder.PriorityOrder.WinningOverrides<IActorValueInformationGetter>())
                {
                    if (avi.Skill != null && avi.EditorID != null && list.Contains(avi.EditorID))
                    {
                        if (avi.Skill != null && ( avi.Skill.UseMult != 0.0f || avi.Skill.OffsetMult != 0.0f || avi.Skill.ImproveOffset < 9999.0f ) )
                        {
                            var pavi = state.PatchMod.ActorValueInformation.GetOrAddAsOverride(avi);
                            if (pavi.Skill != null)
                            {
                                pavi.Skill.UseMult = 0.0f;
                                pavi.Skill.OffsetMult = 0.0f;
                                pavi.Skill.ImproveOffset = 9999.0f;
                                Console.WriteLine($@"{pavi.EditorID}: UseMult [{avi.Skill.UseMult} => {pavi.Skill.UseMult}] OffsetMult [{avi.Skill.OffsetMult} => {pavi.Skill.OffsetMult}] ImproveOffset [{avi.Skill.ImproveOffset} => {pavi.Skill.ImproveOffset}]");
                            }
                        }
                    }
                }
                any = true;
            }

            double mult = Settings.SkillSettings.SkillBooksValueMultiplier / 100.0;
            Console.WriteLine($@"Settings.SkillSettings.PatchSkillBooks is {Settings.SkillSettings.PatchSkillBooks}\r\n + Value multiplier is: {mult}x");
            if (Settings.SkillSettings.PatchSkillBooks)
            {
                foreach (var book in state.LoadOrder.PriorityOrder.WinningOverrides<IBookGetter>())
                {
                    if (book.Flags.HasFlag((Book.Flag)BookTeachesSkill))
                    {
                        var pb = state.PatchMod.Books.GetOrAddAsOverride(book);
                        pb.Teaches?.Clear();
                        if(pb.Flags.HasFlag((Book.Flag)BookTeachesSkill))
                        {
                            pb.Flags ^= (Book.Flag)BookTeachesSkill;
                        }
                        pb.Value = (uint)(pb.Value * mult);
                    }
                }
                any = true;
            }

            return any;
        }
        private readonly static int BookTeachesSkill = 1;
    }
}
