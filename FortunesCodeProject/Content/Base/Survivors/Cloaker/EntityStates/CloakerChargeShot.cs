using FortunesFromTheScrapyard.Survivors.Cloaker;
using FortunesFromTheScrapyard;
using EntityStates;
using MSU;
using MSU.Config;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using CloakerContent = FortunesFromTheScrapyard.Survivors.Cloaker.CloakerSurvivor;

namespace EntityStates.Cloaker.Weapon
{
    public class CloakerChargeShot : BaseSkillState
    {
        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 0f, 30f, configDescOverride = "Fully charged shot damage coefficient. 5 deals 500% damage.", configSectionOverride = "Cloaker - Shoot Harder", configNameOverride = "Maximum Damage Coefficient")]
        [FormatToken(CloakerContent.SECONDARYTOKEN, FormatTokenAttribute.OperationTypeEnum.MultiplyByN, 100, 0)]
        public static float MaxDamageCoefficient = 5f;

        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 0.01f, 10f, configDescOverride = "Full charge time in seconds before attack speed scaling.", configSectionOverride = "Cloaker - Shoot Harder", configNameOverride = "Full Charge Time")]
        [FormatToken(CloakerContent.SECONDARYTOKEN, 1)]
        public static float BaseDuration = 1f;

        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 0f, 10f, configDescOverride = "Minimum seconds before releasing a partially charged shot. A full charge can fire sooner.", configSectionOverride = "Cloaker - Shoot Harder", configNameOverride = "Minimum Charge Time")]
        public static float MinimumChargeDuration = 0.5f;

        private float duration;
        private float? cloakedDamage;
        private uint chargeSoundId;
        private CloakerController controller;
        public override void OnEnter()
        {
            bool captureStealthDamage = isAuthority && !cloakedDamage.HasValue && characterBody.hasCloakBuff;
            if (captureStealthDamage && characterBody.statsDirty)
                characterBody.RecalculateStats();
            base.OnEnter();
            controller = GetComponent<CloakerController>();
            if (captureStealthDamage)
                cloakedDamage = controller.GetStealthAttackDamage(damageStat);
            duration = BaseDuration / attackSpeedStat;
            controller.BreakStealth();
            PlayCrossfade("Gesture, Override", controller.GetAnimationStateName("Gesture, Override", "EnterSecondary"),
                "Secondary.playbackRate", duration, duration * 0.05f);
            chargeSoundId = Util.PlaySound(CloakerAssets.ChargeSound, gameObject);
            characterBody.SetSpreadBloom(0f, false);
            StartAimMode(duration + 2f);
        }
        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (isAuthority && ((!IsKeyDownAuthority() && fixedAge >= MinimumChargeDuration) || fixedAge >= duration))
                outer.SetNextState(new CloakerShoot
                {
                    damageCoefficient = MaxDamageCoefficient * Mathf.Clamp01(fixedAge / duration),
                    charged = true,
                    cloakedDamage = cloakedDamage
                });
        }
        public override void Update()
        {
            base.Update();
            characterBody.SetSpreadBloom(Mathf.Lerp(0f, 1f, Mathf.Clamp01(age / duration)), false);
        }
        public override void OnExit()
        {
            if (chargeSoundId != 0)
            {
                AkSoundEngine.StopPlayingID(chargeSoundId);
                chargeSoundId = 0;
            }
            characterBody.SetSpreadBloom(0f, false);
            cloakedDamage = null;
            if (!outer.destroying)
                PlayAnimation("Gesture, Override", controller.GetAnimationStateName("Gesture, Override", "BufferEmpty"));
            base.OnExit();
        }
        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;

        public override void Reset()
        {
            base.Reset();
            cloakedDamage = null;
        }

        public override void OnSerialize(NetworkWriter writer)
        {
            base.OnSerialize(writer);
            writer.Write(cloakedDamage.HasValue);
            writer.Write(cloakedDamage.GetValueOrDefault());
        }

        public override void OnDeserialize(NetworkReader reader)
        {
            base.OnDeserialize(reader);
            bool hasCloakedDamage = reader.ReadBoolean();
            float damage = reader.ReadSingle();
            cloakedDamage = hasCloakedDamage ? (float?)damage : null;
        }
    }
}
