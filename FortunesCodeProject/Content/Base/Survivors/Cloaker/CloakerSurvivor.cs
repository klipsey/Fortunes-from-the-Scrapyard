using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MSU;
using MSU.Config;
using R2API;
using RoR2;
using RoR2.ContentManagement;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;

namespace FortunesFromTheScrapyard.Survivors.Cloaker
{
    public class CloakerSurvivor : FFTSSurvivor
    {
        public const string PASSIVETOKEN = "FFTS_CLOAKER_PASSIVE_DESCRIPTION";
        public const string AKIMBOTOKEN = "FFTS_CLOAKER_PASSIVE_ALT1_DESCRIPTION";
        public const string PRIMARYTOKEN = "FFTS_CLOAKER_PRIMARY_DESCRIPTION";
        public const string SECONDARYTOKEN = "FFTS_CLOAKER_SECONDARY_DESCRIPTION";
        public const string UTILITYTOKEN = "FFTS_CLOAKER_UTILITY_DESCRIPTION";
        public const string MARKTOKEN = "FFTS_CLOAKER_SPECIAL_DESCRIPTION";
        public const string SCREECHTOKEN = "FFTS_CLOAKER_SPECIAL2_DESCRIPTION";
        public const string MARKKEYWORDTOKEN = "FFTS_CLOAKER_MARK_KEYWORD";

        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 0f, 10f, configDescOverride = "Damage bonus for the next attack that breaks stealth. 1.5 grants 150% bonus damage.", configSectionOverride = "Cloaker - Cloak", configNameOverride = "Stealth Attack Damage Bonus")]
        [FormatToken(PASSIVETOKEN, FormatTokenAttribute.OperationTypeEnum.MultiplyByN, 100, 1)]
        public static float CloakDamageBonus = 1.5f;

        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 0f, 10f, configDescOverride = "Additive attack speed bonus with Akimbo. 1.5 grants 150% bonus attack speed.", configSectionOverride = "Cloaker - Akimbo", configNameOverride = "Attack Speed Bonus")]
        [FormatToken(AKIMBOTOKEN, FormatTokenAttribute.OperationTypeEnum.MultiplyByN, 100, 0)]
        public static float AkimboAttackSpeedBonus = 1.5f;

        [FormatToken(MARKTOKEN, 2)]
        [FormatToken(MARKKEYWORDTOKEN, 2)]
        private const float MarkDescriptionReapplyDelay = 5f;

        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 1f, 10f, configDescOverride = "Total damage multiplier for Mark. The triggering hit stays unchanged; Mark deals the additional damage as a separate hit.", configSectionOverride = "Cloaker - Target", configNameOverride = "Mark Damage Multiplier")]
        [FormatToken(MARKTOKEN, 1)]
        [FormatToken(MARKKEYWORDTOKEN, 1)]
        public static float MarkDamageMultiplier = 2f;

        [FormatToken(MARKTOKEN, FormatTokenAttribute.OperationTypeEnum.MultiplyByN, 100, 3)]
        [FormatToken(MARKKEYWORDTOKEN, FormatTokenAttribute.OperationTypeEnum.MultiplyByN, 100, 3)]
        private const float MarkDescriptionDamageCoefficient = 4f;

        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 0f, 30f, configDescOverride = "Stealth duration for the attacker who kills a target with a Mark-consuming hit.", configSectionOverride = "Cloaker - Target", configNameOverride = "Kill Reward Stealth Duration")]
        [FormatToken(MARKTOKEN, 4)]
        [FormatToken(MARKKEYWORDTOKEN, 4)]
        public static float MarkKillStealthDuration = 4f;

        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 0f, 120f, restartRequired = true, configDescOverride = "Mark's recharge time in seconds. Requires a restart.", configSectionOverride = "Cloaker - Target", configNameOverride = "Cooldown")]
        public static float MarkSkillCooldown = 8f;

        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 0f, 60f, restartRequired = true, configDescOverride = "Shoot Harder's recharge time in seconds. Requires a restart.", configSectionOverride = "Cloaker - Shoot Harder", configNameOverride = "Cooldown")]
        public static float SecondaryCooldown = 4f;

        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 0f, 60f, restartRequired = true, configDescOverride = "Restealth's recharge time in seconds. Requires a restart.", configSectionOverride = "Cloaker - Restealth", configNameOverride = "Cooldown")]
        public static float UtilityCooldown = 7f;

        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 0f, 120f, restartRequired = true, configDescOverride = "Screech's recharge time in seconds. Requires a restart.", configSectionOverride = "Cloaker - Screech", configNameOverride = "Cooldown")]
        public static float ScreechCooldown = 8f;

        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 1f, 10f, restartRequired = true, configDescOverride = "Shoot Harder's maximum stock. Requires a restart.", configSectionOverride = "Cloaker - Shoot Harder", configNameOverride = "Maximum Stock")]
        public static int SecondaryStock = 1;

        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 1f, 10f, restartRequired = true, configDescOverride = "Restealth's maximum stock. Requires a restart.", configSectionOverride = "Cloaker - Restealth", configNameOverride = "Maximum Stock")]
        public static int UtilityStock = 1;

        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 1f, 10f, restartRequired = true, configDescOverride = "Screech's maximum stock. Requires a restart.", configSectionOverride = "Cloaker - Screech", configNameOverride = "Maximum Stock")]
        public static int ScreechStock = 1;

        public static DamageAPI.ModdedDamageType CloakerChargedDamageType;
        public static DamageAPI.ModdedDamageType CloakerScreechDamageType;

        private static DamageAPI.ModdedDamageType markBonusDamageType;
        private static DamageColorIndex markDamageColor;
        private readonly Dictionary<DamageInfo, MarkConsumption> markConsumptions = new Dictionary<DamageInfo, MarkConsumption>();

        private sealed class MarkConsumption
        {
            internal HealthComponent victim;
            internal CharacterBody attacker;
            internal DamageInfo info;
            internal DamageInfo bonusInfo;
            internal float bonusDamage;
            internal bool consumed;
            internal bool killed;
            internal bool rewarded;

            internal bool Consume(DamageReport report)
            {
                if (consumed || info.rejected || !(info.procCoefficient > 0f)
                    || !attacker || attacker.bodyIndex != BodyCatalog.FindBodyIndex("CloakerBody")
                    || !attacker.master || !attacker.master.inventory
                    || !victim.body.HasBuff(FFTSContent.Buffs.bdCloakerMarked)) return false;
                float damage = info.damage;
                if (report.attackerTeamIndex == report.victimTeamIndex)
                {
                    TeamDef team = TeamCatalog.GetTeamDef(report.attackerTeamIndex);
                    if (team != null) damage *= team.friendlyFireScaling;
                }
                if (!(damage > 0f)) return false;
                consumed = true;
                victim.body.RemoveBuff(FFTSContent.Buffs.bdCloakerMarked);
                bonusDamage = info.damage * (MarkDamageMultiplier - 1f);
                return true;
            }
        }

        public override void Initialize()
        {
            CloakerChargedDamageType = DamageAPI.ReserveDamageType();
            CloakerScreechDamageType = DamageAPI.ReserveDamageType();
            markBonusDamageType = DamageAPI.ReserveDamageType();
            markDamageColor = ColorsAPI.RegisterDamageColor(new Color32(0x91, 0x79, 0xB9, 0xFF));
            CloakerAssets.Initialize(assetCollection);
            UnlockableDef unlockable = ScriptableObject.CreateInstance<UnlockableDef>();
            unlockable.cachedName = Unlocks.Cloaker.CloakerUnlockAchievement.UnlockableIdentifier;
            unlockable.nameToken = "FFTS_CLOAKER_BODY_NAME";
            if (!(characterPrefab.GetComponent<CharacterBody>().portraitIcon is Texture2D portrait))
                throw new InvalidOperationException("Cloaker's unlock achievement requires a portrait texture.");
            unlockable.achievementIcon = Sprite.Create(portrait, new Rect(0f, 0f, portrait.width, portrait.height),
                new Vector2(0.5f, 0.5f));
            FFTSContent.fftsContentPack.unlockableDefs.AddSingle(unlockable);
            survivorDef.unlockableDef = unlockable;
            AddSkillUnlock(characterPrefab.GetComponent<CloakerController>().passiveSkillSlot.skillFamily,
                characterPrefab.GetComponent<CloakerController>().passiveAkimboSkillDef,
                Unlocks.Cloaker.CloakerAkimboAchievement.UnlockableIdentifier);
            SkillFamily special = characterPrefab.GetComponent<SkillLocator>().special.skillFamily;
            AddSkillUnlock(special, special.variants.Single(variant => variant.skillDef.skillDescriptionToken == SCREECHTOKEN).skillDef,
                Unlocks.Cloaker.CloakerScreechAchievement.UnlockableIdentifier);
            ModifyPrefab();
            FFTSMain.instance.StartCoroutine(ConfigureSkills());
            On.RoR2.SurvivorCatalog.Init += SurvivorCatalog_Init;
            On.RoR2.HealthComponent.TakeDamageProcess += TakeDamage;
            On.RoR2.HealthComponent.SendDamageDealt += MarkDamageColor;
            On.RoR2.DeathRewards.OnKilledServer += MarkDeathRewards;
            On.RoR2.CharacterBody.OnDeathStart += BodyDeathStarted;
            GlobalEventManager.onServerDamageDealt += DamageDealt;
            GlobalEventManager.onCharacterDeathGlobal += MarkKilled;
            RecalculateStatsAPI.GetStatCoefficients += RecalculateStats;
        }

        private static void AddSkillUnlock(SkillFamily family, SkillDef skill, string identifier)
        {
            UnlockableDef unlockable = ScriptableObject.CreateInstance<UnlockableDef>();
            unlockable.cachedName = identifier;
            unlockable.nameToken = skill.skillNameToken;
            unlockable.achievementIcon = skill.icon;
            FFTSContent.fftsContentPack.unlockableDefs.AddSingle(unlockable);
            int index = Array.FindIndex(family.variants, variant => variant.skillDef == skill);
            if (index < 0) throw new InvalidOperationException($"Cloaker skill {skill.skillNameToken} was not found in its family.");
            family.variants[index].unlockableDef = unlockable;
        }

        public void ModifyPrefab()
        {
            characterPrefab.GetComponent<CharacterBody>().bodyColor = new Color32(60, 60, 68, 255);
            CameraTargetParams camera = characterPrefab.GetComponent<CameraTargetParams>();
            if (!camera.cameraParams)
                camera.cameraParams = CloakerAssets.LoadAddress<CharacterCameraParams>("RoR2/Base/Common/ccpStandard.asset");
            characterPrefab.GetComponent<CharacterBody>().preferredPodPrefab =
                CloakerAssets.LoadAddress<GameObject>("RoR2/Base/SurvivorPod/SurvivorPod.prefab");
            characterPrefab.GetComponent<ModelLocator>().modelTransform.GetComponent<FootstepHandler>().footstepDustPrefab =
                CloakerAssets.LoadAddress<GameObject>("RoR2/Base/Common/VFX/GenericFootstepDust.prefab");
            CloakerAssets.ConfigureAudio(characterPrefab);

            ModelSkinController skinController = characterPrefab.GetComponent<ModelLocator>().modelTransform.GetComponent<ModelSkinController>();
            skinController.skins[0].icon = Skins.CreateSkinIcon(new Color(145f / 255f, 121f / 255f, 185f / 255f),
                new Color(60f / 255f, 45f / 255f, 58f / 255f), new Color(27f / 255f, 27f / 255f, 32f / 255f),
                new Color(191f / 255f, 84f / 255f, 1f));
        }

        private IEnumerator ConfigureSkills()
        {
            while (!ConfigSystem.configsBound) yield return null;

            SkillLocator skills = characterPrefab.GetComponent<SkillLocator>();
            SkillDef secondary = skills.secondary.skillFamily.variants[0].skillDef;
            SkillDef utility = skills.utility.skillFamily.variants[0].skillDef;
            SkillDef mark = skills.special.skillFamily.variants.Single(
                variant => variant.skillDef.skillDescriptionToken == MARKTOKEN).skillDef;
            SkillDef screech = skills.special.skillFamily.variants.Single(
                variant => variant.skillDef.skillDescriptionToken == SCREECHTOKEN).skillDef;
            secondary.baseRechargeInterval = SecondaryCooldown;
            secondary.baseMaxStock = SecondaryStock;
            utility.baseRechargeInterval = UtilityCooldown;
            utility.baseMaxStock = UtilityStock;
            mark.baseRechargeInterval = MarkSkillCooldown;
            mark.stockToConsume = 1;
            screech.baseRechargeInterval = ScreechCooldown;
            screech.baseMaxStock = ScreechStock;
            foreach (SkillDef skill in new[] { mark, screech })
                skill.keywordTokens = (skill.keywordTokens ?? Array.Empty<string>()).Append(MARKKEYWORDTOKEN).Distinct().ToArray();
        }

        public override bool IsAvailable(ContentPack contentPack) => true;

        public override FFTSAssetRequest<SurvivorAssetCollection> LoadAssetRequest()
            => FFTSAssets.LoadAssetAsync<SurvivorAssetCollection>("acCloaker", FFTSBundle.Survivors);

        private void SurvivorCatalog_Init(On.RoR2.SurvivorCatalog.orig_Init orig)
        {
            orig();
            if (FFTSMain.emotesInstalled)
                CloakerEmotes.Initialize(characterPrefab, assetCollection.FindAsset<GameObject>("cloaker_emoteskeleton"));
        }

        private void RecalculateStats(CharacterBody body, RecalculateStatsAPI.StatHookEventArgs args)
        {
            if (!body.TryGetComponent(out CloakerController controller)) return;
            if (controller.isAkimbo) args.attackSpeedMultAdd += AkimboAttackSpeedBonus;
            if (controller.isAkimbo && body.HasBuff(FFTSContent.Buffs.bdCloakerAkimboSpeed))
                args.attackSpeedMultAdd += EntityStates.Cloaker.CloakerRestealth.AkimboAttackSpeedBonus;
        }

        private void DamageDealt(DamageReport report)
        {
            if (!NetworkServer.active || !report.victimBody) return;
            CharacterBody victim = report.victimBody;
            if (!report.damageInfo.HasModdedDamageType(markBonusDamageType)
                && markConsumptions.TryGetValue(report.damageInfo, out MarkConsumption consumption)
                && consumption.victim == report.victim && consumption.attacker == report.attackerBody)
                consumption.Consume(report);
            if (report.attackerBody && report.damageInfo.HasModdedDamageType(CloakerScreechDamageType)
                && victim.healthComponent && victim.healthComponent.alive)
            {
                if (!victim.HasBuff(FFTSContent.Buffs.bdCloakerMarked))
                    victim.AddBuff(FFTSContent.Buffs.bdCloakerMarked);
            }
            if (victim.hasCloakBuff && victim.TryGetComponent(out CloakerController controller))
                controller.BreakStealth();
        }

        private void TakeDamage(On.RoR2.HealthComponent.orig_TakeDamageProcess orig, HealthComponent self, DamageInfo info)
        {
            if (!NetworkServer.active || !self || !self.body || info == null || !info.attacker
                || !info.attacker.TryGetComponent(out CharacterBody attacker)
                || !self.body.HasBuff(FFTSContent.Buffs.bdCloakerMarked) || markConsumptions.ContainsKey(info))
            {
                orig(self, info);
                return;
            }
            MarkConsumption consumption = new MarkConsumption { victim = self, attacker = attacker, info = info };
            markConsumptions.Add(info, consumption);
            try
            {
                orig(self, info);
                if (consumption.consumed && self && self.body && self.alive && consumption.bonusDamage > 0f)
                {
                    DamageInfo bonus = new DamageInfo
                    {
                        damage = consumption.bonusDamage,
                        attacker = info.attacker,
                        inflictor = info.inflictor,
                        position = info.position,
                        crit = info.crit,
                        damageType = info.damageType,
                        damageColorIndex = markDamageColor,
                        procCoefficient = 0f,
                        procChainMask = info.procChainMask
                    };
                    bonus.AddModdedDamageType(markBonusDamageType);
                    consumption.bonusInfo = bonus;
                    markConsumptions.Add(bonus, consumption);
                    try
                    {
                        self.TakeDamage(bonus);
                    }
                    finally
                    {
                        markConsumptions.Remove(bonus);
                    }
                }
            }
            finally
            {
                markConsumptions.Remove(info);
                if (consumption.consumed)
                    CompleteMarkConsumption(consumption);
            }
        }

        private void MarkDamageColor(On.RoR2.HealthComponent.orig_SendDamageDealt orig, DamageReport report)
        {
            if (!report.damageInfo.HasModdedDamageType(markBonusDamageType)
                || !markConsumptions.TryGetValue(report.damageInfo, out MarkConsumption consumption)
                || report.damageInfo != consumption.bonusInfo || consumption.victim != report.victim)
            {
                orig(report);
                return;
            }
            DamageColorIndex previous = report.damageInfo.damageColorIndex;
            report.damageInfo.damageColorIndex = markDamageColor;
            try
            {
                orig(report);
            }
            finally
            {
                report.damageInfo.damageColorIndex = previous;
            }
        }

        private bool IsMarkKill(MarkConsumption consumption, DamageReport report)
            => consumption.consumed && (report.damageInfo == consumption.info || report.damageInfo == consumption.bonusInfo)
                && consumption.attacker && report.attackerBody == consumption.attacker
                && report.victim == consumption.victim;

        private void CompleteMarkConsumption(MarkConsumption consumption)
        {
            if (!NetworkServer.active) return;
            if (consumption.killed)
                EffectManager.SpawnEffect(CloakerAssets.SuccessfulConsumeEffect,
                    new EffectData { origin = consumption.info.position }, true);
            else
                EffectManager.SimpleImpactEffect(CloakerAssets.ConsumeEffect, consumption.info.position, Vector3.up, true);
            CharacterBody attacker = consumption.attacker;
            if (!consumption.killed || !attacker || !attacker.healthComponent || !attacker.healthComponent.alive) return;
            attacker.AddTimedBuff(RoR2Content.Buffs.Cloak, MarkKillStealthDuration);
            EffectManager.SimpleSoundEffect(CloakerAssets.StealthSound.index, attacker.corePosition, true);
            if (attacker.skillLocator)
                attacker.skillLocator.ResetSkills();
        }

        private void MarkKilled(DamageReport report)
        {
            if (markConsumptions.TryGetValue(report.damageInfo, out MarkConsumption consumption)
                && IsMarkKill(consumption, report))
                consumption.killed = true;
        }

        private void MarkDeathRewards(On.RoR2.DeathRewards.orig_OnKilledServer orig, DeathRewards self, DamageReport report)
        {
            if (!NetworkServer.active || !report.victim
                || !markConsumptions.TryGetValue(report.damageInfo, out MarkConsumption consumption)
                || !IsMarkKill(consumption, report) || consumption.rewarded)
            {
                orig(self, report);
                return;
            }
            consumption.killed = true;
            consumption.rewarded = true;
            uint gold = self.goldReward;
            uint experience = self.expReward;
            self.goldReward = (uint)Math.Min((ulong)gold * 3, uint.MaxValue);
            self.expReward = (uint)Math.Min((ulong)experience * 3, uint.MaxValue);
            try
            {
                orig(self, report);
            }
            finally
            {
                self.goldReward = gold;
                self.expReward = experience;
            }
        }

        private void BodyDeathStarted(On.RoR2.CharacterBody.orig_OnDeathStart orig, CharacterBody body)
        {
            if (NetworkServer.active && body.HasBuff(FFTSContent.Buffs.bdCloakerMarked))
                body.RemoveBuff(FFTSContent.Buffs.bdCloakerMarked);
            orig(body);
        }
    }
}
