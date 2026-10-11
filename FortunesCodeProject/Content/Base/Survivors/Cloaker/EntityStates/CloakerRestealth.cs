using FortunesFromTheScrapyard.Survivors.Cloaker;
using FortunesFromTheScrapyard;
using EntityStates;
using MSU;
using MSU.Config;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using CloakerContent = FortunesFromTheScrapyard.Survivors.Cloaker.CloakerSurvivor;

namespace EntityStates.Cloaker
{
    public class CloakerRestealth : BaseSkillState
    {
        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 0.01f, 5f, configDescOverride = "Dash and invincibility duration in seconds.")]
        [FormatToken(CloakerContent.UTILITYTOKEN, 0)]
        public static float Duration = 0.3f;

        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 0f, 3f, configDescOverride = "Dash speed coefficient, scaled by jump power and movement speed.")]
        public static float SpeedCoefficient = 0.4f;

        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 0f, 5f, configDescOverride = "Additive attack speed bonus granted by Restealth with Akimbo. 0.15 grants 15%.")]
        [FormatToken(CloakerContent.UTILITYTOKEN, FormatTokenAttribute.OperationTypeEnum.MultiplyByN, 100, 2)]
        public static float AkimboAttackSpeedBonus = 0.15f;

        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 0f, 30f, configDescOverride = "Duration of Restealth's Akimbo attack speed bonus in seconds.")]
        [FormatToken(CloakerContent.UTILITYTOKEN, 3)]
        public static float AkimboAttackSpeedDuration = 4f;

        private Vector3 forwardDirection;
        private float speed;
        private CameraTargetParams.AimRequest cameraRequest;
        private bool invincibilityApplied;
        public override void OnEnter()
        {
            base.OnEnter();

            CloakerController controller = GetComponent<CloakerController>();
            characterBody.SetAimTimer(2f);
            if (cameraTargetParams && cameraTargetParams.cameraParams)
                cameraRequest = cameraTargetParams.RequestAimType(CameraTargetParams.AimType.Aura);
            if (base.isAuthority)
            {
                if (base.inputBank && base.characterDirection)
                {
                    forwardDirection = ((base.inputBank.moveVector == Vector3.zero) ? base.characterDirection.forward : base.inputBank.moveVector).normalized;
                }
            }

            PlayCrossfade("FullBody, Override", controller.GetAnimationStateName("FullBody, Override", "Dash"),
                "Utility.playbackRate", Duration * 4f, 0.05f);
            Util.PlaySound(CloakerAssets.DashSound, gameObject);
            speed = SpeedCoefficient * characterBody.jumpPower * Mathf.Clamp(characterBody.moveSpeed / 4f, 5f, 20f);

            if (isAuthority && characterMotor && characterDirection)
            {
                characterMotor.velocity = forwardDirection * speed;
                characterDirection.moveVector = forwardDirection;
            }

            if (NetworkServer.active)
            {
                characterBody.AddBuff(RoR2Content.Buffs.HiddenInvincibility);
                invincibilityApplied = true;
                if (controller.isAkimbo)
                    characterBody.AddTimedBuff(FFTSContent.Buffs.bdCloakerAkimboSpeed, AkimboAttackSpeedDuration);
            }
            if (!controller.isAkimbo) controller.StartGracePeriod();
        }
        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (isAuthority && characterMotor && characterDirection)
            {
                characterMotor.Motor.ForceUnground();
                characterMotor.velocity = forwardDirection * speed;
            }
            if (isAuthority && fixedAge >= Duration) outer.SetNextStateToMain();
        }
        public override void OnExit()
        {
            cameraRequest?.Dispose();
            if (NetworkServer.active && invincibilityApplied)
                characterBody.RemoveBuff(RoR2Content.Buffs.HiddenInvincibility);
            base.OnExit();
        }
        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Frozen;
    }
}
