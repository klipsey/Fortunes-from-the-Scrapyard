using RoR2;
using RoR2.Achievements;

namespace FortunesFromTheScrapyard.Unlocks.Cloaker
{
    [RegisterAchievement(Identifier, UnlockableIdentifier, CloakerUnlockAchievement.Identifier, 0u)]
    public class CloakerAkimboAchievement : BaseAchievement
    {
        public const string Identifier = "FFTS_CloakerAkimboUnlock";
        public const string UnlockableIdentifier = "Skills.FFTS_Cloaker.Akimbo";

        private bool charging;
        private bool stealthed;

        public override BodyIndex LookUpRequiredBodyIndex() => BodyCatalog.FindBodyIndex("CloakerBody");

        public override void OnBodyRequirementMet()
        {
            base.OnBodyRequirementMet();
            TeleporterInteraction.onTeleporterBeginChargingGlobal += OnTeleporterBeginCharging;
            TeleporterInteraction.onTeleporterChargedGlobal += OnTeleporterCharged;
            RoR2Application.onUpdate += CheckStealth;
        }

        public override void OnBodyRequirementBroken()
        {
            RoR2Application.onUpdate -= CheckStealth;
            TeleporterInteraction.onTeleporterChargedGlobal -= OnTeleporterCharged;
            TeleporterInteraction.onTeleporterBeginChargingGlobal -= OnTeleporterBeginCharging;
            charging = false;
            base.OnBodyRequirementBroken();
        }

        private void OnTeleporterBeginCharging(TeleporterInteraction teleporter)
        {
            charging = true;
            stealthed = false;
            CheckStealth();
        }

        private void CheckStealth()
        {
            CharacterBody body = localUser.cachedBody;
            if (charging && body && body.hasCloakBuff) stealthed = true;
        }

        private void OnTeleporterCharged(TeleporterInteraction teleporter)
        {
            if (!charging) return;
            CheckStealth();
            charging = false;
            CharacterBody body = localUser.cachedBody;
            if (!stealthed && body && body.healthComponent && body.healthComponent.alive)
                Grant();
        }
    }
}
