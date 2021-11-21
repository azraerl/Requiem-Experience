# Requiem-Experience

This is an auto-patcher which is meant to create patch for Experience mod, to be used along with Requiem - or any other overhaul which makes Skyrim world to be un-levelled.

# How to run patcher?
Please follow steps in [Synthesis wiki](https://github.com/Mutagen-Modding/Synthesis/wiki/Installation)

# Patcher logic and settings

It takes care of few things:
* Adjusts quest types for various mods to reduce/increase XP - would only affect quests which present in your load order:
  * Vanilla Skyrim: reduces XP from Helgen & Whiterun main quests;
  * Requiem: removes XP from Requiem install/init quest;
  * Live Another Life: removes XP from Chargen and new life selection;
  * SunHelm: removes XP from SunHelm install/init quest;
  * Missives: here is the place where most of changes takes place, all the missives are being assigned with different quest types depending on their estimated difficulty. Also due to usage of regular expressions (masks), it's able to apply this changes to almost any additional regions (e.g. you have Wyrmstooth and Wyrmstooth Missives? they'll get auto-adjusted as well)
* Creates/overwrites DefaultRaces.csv to assign appropriate values for kill XP, also highly dependant on your load order. General idea is to base of average level of enemy to calculate reward, with some edge cases covered with overrides. Keywords explained:
  * Ignore: NPCs with Editor ID matching regex will be completely ignored while calculating average power of race;
  * Unique: NPCs with Editor ID matching regex will be assigned with new race (generated as "<race EditorID>__<npc EditorID>") and thus will have unique XP value;
  * Groups: NPCs with races in list will be grouped together to average over their power. Basically makes sure that, say, Bosmer and Nord bandits or vampires will have same XP value;
  * Override: this particular Races (not NPCs!) will have its XP set to a constant provided.
* Skills related changes:
  * Suppresses Skill XP gain from use, e.g. disables vanilla skill levelling system. Meant to be used with mods which provide alternative Skill levelling (Requiem - Static Skill Levelling or 3Tweaks, for example). ___Untick this one if you are not planning to use such mods___;
  * Removes Skills from Skill Books thus makes them to be a normal books. Also increases gold value of no-longer-Skill Books (default is 200%/2x)


I have also included 2 proposed Experience ini presets, which share most of the settings apart from killing XP:
* True_Experience.ini - true unleveled XP, which means XP multiplier is 1.0 and level range is 65535. Killing XP doesn't care of your level / level of the enemy.
* Reward_Experience.ini - challenge rewarded... XP multiplier is 0.5 and level range is 10. You get full XP only when kill enemy 10 levels above you, and about 1 XP when kill enemy 10 levels below you. On other hand if enemy is 10+ levels above - you get extra credit.

Pick one via Synthesis Settings, or grab it manually ¯\\\_(ツ)\_/¯
Feel free to edit proposed values if you like so~
