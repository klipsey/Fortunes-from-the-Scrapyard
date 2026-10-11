using MSU;
using MSU.Config;
using RoR2;
using RoR2.ContentManagement;
using System;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace FortunesFromTheScrapyard.Items
{
    public class CounterfeitCurrency : FFTSItem
    {
        [FFTSConfigureField(FFTSConfig.ID_ITEMS, 0f, 10f, configDescOverride = "XP gained per gold spent, per stack, before the team-wide XP penalty.")]
        public static float experiencePerGoldPerStack = 0.5f;

        [FFTSConfigureField(FFTSConfig.ID_ITEMS, 0f, 100f, configDescOverride = "Percentage reduction to all team XP gains, compounded for every stack held by the team. Applies before normal XP shares become gold.")]
        public static float experienceReductionPercentPerStack = 25f;

        private readonly Dictionary<CharacterMaster, double> goldRemainders = new Dictionary<CharacterMaster, double>();
        private readonly double[] experienceRemainders = new double[(int)TeamIndex.Count];
        private readonly HashSet<Inventory> cleansingInventories = new HashSet<Inventory>();
        private PickupDropTable pearlDropTable;
        private bool awardingPurchaseExperience;

        public override void Initialize()
        {
            pearlDropTable = Addressables.LoadAssetAsync<PickupDropTable>(
                RoR2BepInExPack.GameAssetPaths.RoR2_Base_ShrineCleanse.dtPearls_asset).WaitForCompletion();
            if (!pearlDropTable)
                throw new InvalidOperationException("Counterfeit Currency could not load the cleansing pool's pearl drop table.");

            On.RoR2.CostTypeDef.PayCost_PayCostContext_PayCostResults += CostTypeDef_PayCost;
            On.RoR2.TeamManager.GiveTeamExperience += TeamManager_GiveTeamExperience;
            Inventory.onInventoryChangedGlobal += OnInventoryChanged;
            Run.onRunStartGlobal += OnRunStart;
        }

        private void OnRunStart(Run run)
        {
            goldRemainders.Clear();
            Array.Clear(experienceRemainders, 0, experienceRemainders.Length);
            pearlDropTable.Regenerate(run);
            if (NetworkServer.active)
            {
                foreach (CharacterMaster master in CharacterMaster.readOnlyInstancesList)
                {
                    if (master.inventory)
                        OnInventoryChanged(master.inventory);
                }
            }
        }

        private void OnInventoryChanged(Inventory inventory)
        {
            if (!NetworkServer.active || !Run.instance || cleansingInventories.Contains(inventory))
                return;

            var counterfeit = new Inventory.ItemTransformation
            {
                originalItemIndex = FFTSContent.Items.CounterfeitCurrency.itemIndex,
                allowWhenDisabled = true
            };
            var solitude = new Inventory.ItemTransformation
            {
                originalItemIndex = DLC2Content.Items.OnLevelUpFreeUnlock.itemIndex,
                allowWhenDisabled = true
            };

            if (!counterfeit.CanTake(inventory, out _) || !solitude.CanTake(inventory, out _))
                return;

            cleansingInventories.Add(inventory);
            try
            {
                inventory.InventoryChangedHandler(changedInventory =>
                {
                    while (counterfeit.CanTake(changedInventory, out _) && solitude.CanTake(changedInventory, out _))
                    {
                        counterfeit.newItemIndex = RollPearl();
                        solitude.newItemIndex = RollPearl();
                        if (!counterfeit.TryTransform(changedInventory, out _) || !solitude.TryTransform(changedInventory, out _))
                            throw new InvalidOperationException("Counterfeit Currency could not cleanse a matching item pair.");
                    }
                });
            }
            finally
            {
                cleansingInventories.Remove(inventory);
            }
        }

        private ItemIndex RollPearl()
        {
            UniquePickup pickup = pearlDropTable.GeneratePickupPreReplacement(Run.instance.treasureRng);
            ItemIndex itemIndex = PickupCatalog.GetPickupDef(pickup.pickupIndex)?.itemIndex ?? ItemIndex.None;
            if (itemIndex != RoR2Content.Items.Pearl.itemIndex && itemIndex != RoR2Content.Items.ShinyPearl.itemIndex)
                throw new InvalidOperationException("The cleansing pool's drop table did not produce a pearl.");
            return itemIndex;
        }

        private void CostTypeDef_PayCost(On.RoR2.CostTypeDef.orig_PayCost_PayCostContext_PayCostResults orig,
            CostTypeDef self, CostTypeDef.PayCostContext context, CostTypeDef.PayCostResults result)
        {
            CharacterMaster master = context.activatorMaster;
            int itemCount = NetworkServer.active && master && master.inventory &&
                self == CostTypeCatalog.GetCostTypeDef(CostTypeIndex.Money) && context.cost > 0
                ? master.inventory.GetItemCountEffective(FFTSContent.Items.CounterfeitCurrency)
                : 0;
            uint moneyBefore = itemCount > 0 ? master.money : 0;

            orig(self, context, result);

            if (itemCount <= 0 || !master || master.money >= moneyBefore)
                return;

            uint goldSpent = Math.Min(moneyBefore - master.money, (uint)context.cost);
            GetTeamRecipients(master.teamIndex, out double teamStacks);
            double experience = goldSpent * (double)experiencePerGoldPerStack * itemCount * GetExperienceMultiplier(teamStacks);
            ulong award = TakeWholeExperience(master.teamIndex, experience);
            if (award == 0)
                return;

            bool wasAwardingPurchaseExperience = awardingPurchaseExperience;
            awardingPurchaseExperience = true;
            try
            {
                TeamManager.instance.GiveTeamExperience(master.teamIndex, award);
            }
            finally
            {
                awardingPurchaseExperience = wasAwardingPurchaseExperience;
            }
        }

        private void TeamManager_GiveTeamExperience(On.RoR2.TeamManager.orig_GiveTeamExperience orig,
            TeamManager self, TeamIndex teamIndex, ulong experience)
        {
            if (!NetworkServer.active || awardingPurchaseExperience || experience == 0)
            {
                orig(self, teamIndex, experience);
                return;
            }

            List<(CharacterMaster master, int itemCount)> recipients = GetTeamRecipients(teamIndex, out double teamStacks);
            if (teamStacks == 0)
            {
                orig(self, teamIndex, experience);
                return;
            }

            double share = experience * GetExperienceMultiplier(teamStacks) / recipients.Count;
            double remainingExperience = 0;
            foreach (var recipient in recipients)
            {
                if (recipient.itemCount > 0)
                    GiveConvertedGold(recipient.master, share);
                else
                    remainingExperience += share;
            }

            orig(self, teamIndex, TakeWholeExperience(teamIndex, remainingExperience));
        }

        private static List<(CharacterMaster master, int itemCount)> GetTeamRecipients(TeamIndex teamIndex, out double teamStacks)
        {
            var recipients = new List<(CharacterMaster master, int itemCount)>();
            teamStacks = 0;
            foreach (PlayerCharacterMasterController controller in PlayerCharacterMasterController.instances)
            {
                CharacterMaster master = controller.master;
                if (!master || master.teamIndex != teamIndex || !master.inventory)
                    continue;

                int itemCount = master.inventory.GetItemCountEffective(FFTSContent.Items.CounterfeitCurrency);
                recipients.Add((master, itemCount));
                teamStacks += itemCount;
            }
            return recipients;
        }

        private static double GetExperienceMultiplier(double teamStacks)
        {
            return Math.Pow(1.0 - experienceReductionPercentPerStack / 100.0, teamStacks);
        }

        private ulong TakeWholeExperience(TeamIndex teamIndex, double experience)
        {
            int index = (int)teamIndex;
            double total = experience + experienceRemainders[index];
            if (total >= ulong.MaxValue)
            {
                experienceRemainders[index] = 0;
                return ulong.MaxValue;
            }

            ulong whole = (ulong)total;
            experienceRemainders[index] = total - whole;
            return whole;
        }

        private void GiveConvertedGold(CharacterMaster master, double experience)
        {
            goldRemainders.TryGetValue(master, out double remainder);
            double total = experience + remainder;
            double whole = Math.Floor(total);
            goldRemainders[master] = total - whole;
            uint gold = (uint)Math.Min(whole, uint.MaxValue - master.money);
            if (gold == 0)
                return;

            master.GiveMoney(gold, false);
        }

        public override bool IsAvailable(ContentPack contentPack)
        {
            return true;
        }

        public override FFTSAssetRequest LoadAssetRequest()
        {
            return FFTSAssets.LoadAssetAsync<ItemAssetCollection>("acCounterfeitCurrency", FFTSBundle.Items);
        }
    }
}
