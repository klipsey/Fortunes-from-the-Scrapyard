using RoR2;
using RoR2.Achievements;
using UnityEngine;

namespace FortunesFromTheScrapyard.Unlocks.Cloaker
{
    [RegisterAchievement(Identifier, UnlockableIdentifier, null, 0u)]
    public class CloakerUnlockAchievement : BaseAchievement
    {
        public const string Identifier = "FFTS_CloakerUnlock";
        public const string UnlockableIdentifier = "Characters.FFTS_Cloaker";
        private const float RequiredStealthDuration = 30f;
        private const float RevealGraceDuration = 4f;

        private float stealthDuration;
        private float revealedDuration;

        public override void OnInstall()
        {
            base.OnInstall();
            localUser.onBodyChanged += ResetProgress;
            Run.onRunStartGlobal += OnRunChanged;
            Run.onRunDestroyGlobal += OnRunChanged;
            RoR2Application.onUpdate += CheckStealth;
        }

        public override void OnUninstall()
        {
            RoR2Application.onUpdate -= CheckStealth;
            Run.onRunStartGlobal -= OnRunChanged;
            Run.onRunDestroyGlobal -= OnRunChanged;
            localUser.onBodyChanged -= ResetProgress;
            base.OnUninstall();
        }

        private void OnRunChanged(Run run) => ResetProgress();

        private void ResetProgress()
        {
            stealthDuration = 0f;
            revealedDuration = 0f;
        }

        private void CheckStealth()
        {
            if (shouldGrant) return;
            CharacterBody body = localUser.cachedBody;
            if (!Run.instance || !body || !body.healthComponent || !body.healthComponent.alive)
            {
                ResetProgress();
                return;
            }
            if (Run.instance.isRunStopwatchPaused) return;

            if (body.hasCloakBuff)
            {
                revealedDuration = 0f;
                stealthDuration += Time.deltaTime;
                if (stealthDuration >= RequiredStealthDuration)
                    Grant();
            }
            else
            {
                revealedDuration += Time.deltaTime;
                if (revealedDuration > RevealGraceDuration)
                    ResetProgress();
            }
        }
    }
}
