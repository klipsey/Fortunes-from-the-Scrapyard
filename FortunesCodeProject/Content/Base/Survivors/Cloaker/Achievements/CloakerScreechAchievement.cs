using EntityStates.Cloaker;
using RoR2;
using RoR2.Achievements;

namespace FortunesFromTheScrapyard.Unlocks.Cloaker
{
    [RegisterAchievement(Identifier, UnlockableIdentifier, CloakerUnlockAchievement.Identifier, 0u)]
    public class CloakerScreechAchievement : BaseAchievement
    {
        public const string Identifier = "FFTS_CloakerScreechUnlock";
        public const string UnlockableIdentifier = "Skills.FFTS_Cloaker.Screech";
        private const int RequiredMarks = 5;

        private int marks;

        public override BodyIndex LookUpRequiredBodyIndex() => BodyCatalog.FindBodyIndex("CloakerBody");

        public override void OnBodyRequirementMet()
        {
            base.OnBodyRequirementMet();
            marks = 0;
            CloakerMark.onAuthorityMarkCast += OnMarkCast;
            RoR2Application.onUpdate += CheckStealth;
        }

        public override void OnBodyRequirementBroken()
        {
            RoR2Application.onUpdate -= CheckStealth;
            CloakerMark.onAuthorityMarkCast -= OnMarkCast;
            marks = 0;
            base.OnBodyRequirementBroken();
        }

        private void CheckStealth()
        {
            CharacterBody body = localUser.cachedBody;
            if (!body || !body.hasCloakBuff) marks = 0;
        }

        private void OnMarkCast(CharacterBody body)
        {
            if (body != localUser.cachedBody || !body.hasCloakBuff) return;
            if (++marks >= RequiredMarks) Grant();
        }
    }
}
