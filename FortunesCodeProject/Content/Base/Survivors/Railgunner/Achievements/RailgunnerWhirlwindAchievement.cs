using RoR2;
using RoR2.Achievements;

namespace FortunesFromTheScrapyard.Unlocks.Railgunner
{
    [RegisterAchievement(Identifier, UnlockableIdentifier, null, 10u)]
    public sealed class RailgunnerWhirlwindAchievement : BaseAchievement
    {
        public const string Identifier = "FFTS_RailgunnerWhirlwind";
        public const string UnlockableIdentifier = "Skins.FFTS.Railgunner.Archer";

        public override BodyIndex LookUpRequiredBodyIndex()
        {
            return BodyCatalog.FindBodyIndex("RailgunnerBody");
        }

        public override void OnBodyRequirementMet()
        {
            base.OnBodyRequirementMet();
            Run.onClientGameOverGlobal += OnClientGameOverGlobal;
        }

        public override void OnBodyRequirementBroken()
        {
            Run.onClientGameOverGlobal -= OnClientGameOverGlobal;
            base.OnBodyRequirementBroken();
        }

        public override void OnUninstall()
        {
            Run.onClientGameOverGlobal -= OnClientGameOverGlobal;
            base.OnUninstall();
        }

        private void OnClientGameOverGlobal(Run run, RunReport report)
        {
            if (report.gameEnding && report.gameEnding.isWin && Whirlwind.whirlwindDifficulty
                && DifficultyCatalog.GetDifficultyDef(report.ruleBook.FindDifficulty()) == Whirlwind.whirlwindDifficulty.DifficultyDef)
                Grant();
        }
    }
}
