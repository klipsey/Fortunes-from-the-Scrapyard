using Mono.Cecil.Cil;
using MonoMod.Cil;
using MSU;
using MSU.Config;
using RoR2;
using RoR2.ContentManagement;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace FortunesFromTheScrapyard.Items
{
    public class Multitool : FFTSItem
    {
        public const string TOKEN = "FFTS_ITEM_MULTITOOL_DESCRIPTION";

        // BodyFlags.Drone is not named in the project's compile-time game libraries.
        private const CharacterBody.BodyFlags droneBodyFlag = (CharacterBody.BodyFlags)0x400000;
        private GameObject droneRemnantPrefab;
        private readonly Dictionary<SummonMasterBehavior, CharacterMaster> dronePurchases =
            new Dictionary<SummonMasterBehavior, CharacterMaster>();

        [FFTSConfigureField(FFTSConfig.ID_ITEMS, restartRequired = true, configDescOverride = "Enable legacy effects on non-drone interactables. Individual shrine, terminal, and minion settings still apply.")]
        public static bool legacyInteractables = false;
        [FFTSConfigureField(FFTSConfig.ID_ITEMS, restartRequired = true)]
        public static bool shrineBlood = true;
        [FFTSConfigureField(FFTSConfig.ID_ITEMS, restartRequired = true)]
        public static bool shrineMountain = true;
        [FFTSConfigureField(FFTSConfig.ID_ITEMS, restartRequired = true)]
        public static bool shrineChance = true;
        [FFTSConfigureField(FFTSConfig.ID_ITEMS, restartRequired = true)]
        public static bool shrineCombat = false;
        [FFTSConfigureField(FFTSConfig.ID_ITEMS, restartRequired = true, configDescOverride = "Includes printers and shops.")]
        public static bool terminals = true;
        [FFTSConfigureField(FFTSConfig.ID_ITEMS, restartRequired = true, configDescOverride = "Enable Multitool duplication for purchased drones and gunner turrets. Other summoned minions require Legacy Interactables.")]
        public static bool minions = true;
        [FFTSConfigureField(FFTSConfig.ID_ITEMS, restartRequired = true, configDescOverride = "Leave one-use remnants after purchasing drones and gunner turrets, allowing Multitool to be used on them later. Disabled by default: an active Multitool charge duplicates the drone immediately on purchase instead.")]
        public static bool droneRemnants = false;


        //[ConfigureField(FFTSConfig.ID_ITEMS, configDescOverride = "Includes chests and scavenger bags.")]
        //public static bool chests = true;
        //[ConfigureField(FFTSConfig.ID_ITEMS)]
        //public static bool roulette = true;
        public override void Initialize()
        {
            droneRemnantPrefab = MultitoolDroneRemnant.CreatePrefab();
            FFTSMain.instance.StartCoroutine(FFTSConfig.AfterConfigsBound(InstallHooks));
        }

        public override void ModifyContentPack(ContentPack contentPack)
        {
            base.ModifyContentPack(contentPack);
            contentPack.networkedObjectPrefabs.AddSingle(droneRemnantPrefab);
        }

        private void InstallHooks()
        {
            On.RoR2.PurchaseInteraction.OnInteractionBegin += MultitoolInteract;
            if (legacyInteractables)
            {
                if (terminals)
                    On.RoR2.ShopTerminalBehavior.DropPickup += MultitoolTerminal;
                if (shrineMountain)
                    On.RoR2.ShrineBossBehavior.AddShrineStack += MultitoolMountain;
                if (shrineBlood)
                    On.RoR2.ShrineBloodBehavior.AddShrineStack += MultitoolBlood;
                if (shrineChance)
                    IL.RoR2.ShrineChanceBehavior.AddShrineStack += MultitoolChance;
                if (shrineCombat)
                    IL.RoR2.ShrineCombatBehavior.AddShrineStack += MultitoolCombat;
            }
            if (minions)
                On.RoR2.SummonMasterBehavior.OpenSummonReturnMaster += MultitoolMinions;
            //if (chests)
            //    On.RoR2.ChestBehavior.ItemDrop += MultitoolChest;
            //if (roulette)
            //    On.RoR2.RouletteChestController.EjectPickupServer += MultitoolRoulette;

            On.RoR2.CharacterMaster.OnServerStageBegin += TryRegenerateMultitool;
        }

        
        public override bool IsAvailable(ContentPack contentPack)
        {
            return true;
        }

        public override FFTSAssetRequest LoadAssetRequest()
        {
            return FFTSAssets.LoadAssetAsync<ItemAssetCollection>("acMultitool", FFTSBundle.Items);
        }

        private CharacterMaster MultitoolMinions(On.RoR2.SummonMasterBehavior.orig_OpenSummonReturnMaster orig, SummonMasterBehavior self, Interactor activator)
        {
            CharacterMaster result = orig(self, activator);

            if (NetworkServer.active && result && dronePurchases.ContainsKey(self))
            {
                dronePurchases[self] = result;
            }
            else if (NetworkServer.active && result && legacyInteractables && !IsDroneSummon(self) &&
                self.TryGetComponent(out MultitoolComponent mtc) && mtc.VerifyMultitoolAndBreak())
            {
                orig(self,activator);
            }

            return result;
        }

        internal static bool IsDroneSummon(SummonMasterBehavior summon)
        {
            if (!summon.masterPrefab || !summon.masterPrefab.TryGetComponent(out CharacterMaster master) ||
                !master.bodyPrefab || !master.bodyPrefab.TryGetComponent(out CharacterBody body))
                return false;

            return (body.bodyFlags & droneBodyFlag) != CharacterBody.BodyFlags.None ||
                master.bodyPrefab.name == "Turret1Body";
        }

        private void MultitoolCombat(ILContext il)
        {
            ILCursor c = new ILCursor(il);

            c.GotoNext(MoveType.After,
                x => x.MatchCallOrCallvirt<ShrineCombatBehavior>("get_monsterCredit")
                );
            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Func<float, ShrineCombatBehavior, float>>((monsterCredit, self) =>
            {
                bool multitoolSuccess = false;
                MultitoolComponent mtc = null;
                if (self.TryGetComponent(out mtc))
                {
                    if (mtc.VerifyMultitoolAndBreak())
                    {
                        multitoolSuccess = true;

                        return monsterCredit * 2;
                    }
                }
                return monsterCredit;
            });
        }

        private void MultitoolTerminal(On.RoR2.ShopTerminalBehavior.orig_DropPickup orig, ShopTerminalBehavior self)
        {
            orig(self);

            if (NetworkServer.active)
            {
                MultitoolComponent mtc = null;
                if (self.TryGetComponent(out mtc))
                {
                    if (mtc.VerifyMultitoolAndBreak())
                    {
                        PickupDropletController.CreatePickupDroplet(self.pickup,
                            (self.dropTransform ? self.dropTransform : self.transform).position,
                            self.transform.TransformVector(self.dropVelocity));
                    }
                }
            }
        }

        private void MultitoolRoulette(On.RoR2.RouletteChestController.orig_EjectPickupServer orig, RouletteChestController self, UniquePickup pickup)
        {
            orig(self, pickup);
            if (!pickup.isValid)
            {
                return;
            }
            if (NetworkServer.active)
            {
                MultitoolComponent mtc = null;
                if (self.TryGetComponent(out mtc))
                {
                    if (mtc.VerifyMultitoolAndBreak())
                    {
                        PickupDropletController.CreatePickupDroplet(pickup, self.ejectionTransform.position, self.ejectionTransform.rotation * (self.localEjectionVelocity + new Vector3(-2, 0, 0)));
                    }
                }
            }
        }

        private void MultitoolChance(ILContext il)
        {
            ILCursor c = new ILCursor(il);

            c.GotoNext(MoveType.Before,
                x => x.MatchCallOrCallvirt<PickupDropletController>(nameof(PickupDropletController.CreatePickupDroplet))
                );
            c.Remove();
            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Action<UniquePickup, Vector3, Vector3, ShrineChanceBehavior>>((pickup, dropletOrigin, dropletDirection, self) =>
            {
                bool multitoolSuccess = false;
                MultitoolComponent mtc = null;
                if (self.TryGetComponent(out mtc))
                {
                    if (mtc.VerifyMultitoolAndBreak())
                    {
                        multitoolSuccess = true;

                        float angle = 45;
                        Vector3 vector = dropletDirection;
                        Quaternion rotation = Quaternion.AngleAxis(-angle / 2, Vector3.up);
                        vector = rotation * vector;
                        rotation = Quaternion.AngleAxis(angle, Vector3.up);
                        for (int i = 0; i < 2; i++)
                        {
                            PickupDropletController.CreatePickupDroplet(pickup, dropletOrigin, vector);
                            vector = rotation * vector;
                        }
                    }
                }
                if (!multitoolSuccess)
                {
                    PickupDropletController.CreatePickupDroplet(pickup, dropletOrigin, dropletDirection);
                }
            });
        }

        private void MultitoolBlood(On.RoR2.ShrineBloodBehavior.orig_AddShrineStack orig, ShrineBloodBehavior self, Interactor interactor)
        {
            if (NetworkServer.active)
            {
                MultitoolComponent mtc = null;
                if (self.TryGetComponent(out mtc))
                {
                    if (mtc.VerifyMultitoolAndBreak())
                    {
                        self.purchaseCount--;
                        self.AddShrineStack(interactor);
                    }
                }
            }

            orig(self, interactor);
        }

        private void MultitoolMountain(On.RoR2.ShrineBossBehavior.orig_AddShrineStack orig, ShrineBossBehavior self, Interactor interactor)
        {
            if (NetworkServer.active)
            {
                MultitoolComponent mtc = null;
                if (self.TryGetComponent(out mtc))
                {
                    if (mtc.VerifyMultitoolAndBreak())
                    {
                        self.purchaseCount--;
                        self.AddShrineStack(interactor);
                    }
                }
            }

            orig(self, interactor);
        }

        private void MultitoolChest(On.RoR2.ChestBehavior.orig_ItemDrop orig, ChestBehavior self)
        {
            if (NetworkServer.active)
            {
                MultitoolComponent mtc = null;
                if (self.TryGetComponent(out mtc))
                {
                    if (mtc.VerifyMultitoolAndBreak())
                    {
                        self.dropCount += 1;
                    }
                }
            }
            orig(self);
        }

        private void MultitoolInteract(On.RoR2.PurchaseInteraction.orig_OnInteractionBegin orig, RoR2.PurchaseInteraction self, RoR2.Interactor activator)
        {
            if (!self.CanBeAffordedByInteractor(activator))
            {
                return;
            }

            if (NetworkServer.active && minions &&
                self.TryGetComponent(out SummonMasterBehavior summon) && IsDroneSummon(summon))
            {
                dronePurchases.Add(summon, null);
                CharacterMaster purchasedMaster;
                try
                {
                    orig(self, activator);
                    purchasedMaster = dronePurchases[summon];
                }
                finally
                {
                    dronePurchases.Remove(summon);
                }

                if (purchasedMaster)
                {
                    if (droneRemnants)
                        MultitoolDroneRemnant.Spawn(droneRemnantPrefab, summon, purchasedMaster);
                    else
                        DuplicatePurchasedDrone(summon, purchasedMaster, activator.GetComponent<CharacterBody>());
                }
                return;
            }

            if (NetworkServer.active && legacyInteractables &&
                activator.gameObject.TryGetComponent(out CharacterBody activatorBody) && activatorBody.inventory)
            {
                Inventory inv = activatorBody.inventory;
                int multitoolCount = inv.GetItemCount(FFTSContent.Items.Multitool);
                if (multitoolCount > 0)
                {
                    MultitoolComponent mtc = self.gameObject.GetComponent<MultitoolComponent>();
                    if (mtc == null)
                        mtc = self.gameObject.AddComponent<MultitoolComponent>();

                    mtc.SetInteractor(activatorBody, inv);
                }
            }

            orig(self, activator);
        }

        private static void DuplicatePurchasedDrone(SummonMasterBehavior purchase, CharacterMaster purchasedMaster, CharacterBody body)
        {
            if (!HasActiveCharge(body))
                return;

            Vector3 position = purchase.transform.position;
            RoR2.Navigation.NodeGraph.NodeIndex node = RoR2.Navigation.NodeGraph.NodeIndex.invalid;
            bool isTurret = purchasedMaster.bodyPrefab.name == "Turret1Body";
            if (isTurret && !MultitoolDroneRemnant.TryFindTurretPosition(position,
                MultitoolDroneRemnant.GetPlacementRadius(purchasedMaster), null, out position, out node))
            {
                FFTSLog.Warning("Multitool turret duplication needs an unoccupied ground node. No charge was consumed.");
                return;
            }

            ItemIndex upgradeItem = ItemCatalog.FindItemIndex("DroneUpgradeHidden");
            int upgradeCount = upgradeItem != ItemIndex.None ? purchasedMaster.inventory.GetItemCount(upgradeItem) : 0;
            EquipmentIndex equipmentIndex = purchasedMaster.inventory.GetEquipmentIndex();
            if (!TryConsumeCharge(body))
                return;

            CharacterMaster spawnedMaster = null;
            try
            {
                spawnedMaster = new MasterSummon
                {
                    masterPrefab = purchase.masterPrefab,
                    position = position,
                    rotation = purchase.transform.rotation,
                    summonerBodyObject = body.gameObject,
                    ignoreTeamMemberLimit = true,
                    useAmbientLevel = true,
                    preSpawnSetupCallback = master => MultitoolDroneRemnant.SetupInventory(master, upgradeCount, equipmentIndex)
                }.Perform();
                if (!spawnedMaster)
                {
                    FFTSLog.Error("Multitool could not summon the duplicate drone. The charge was refunded.");
                    return;
                }
                if (isTurret)
                    DirectorCore.instance.AddOccupiedNode(SceneInfo.instance.groundNodes, node);
            }
            finally
            {
                if (!spawnedMaster)
                    RefundCharge(body);
            }
        }

        internal static bool HasActiveCharge(CharacterBody body)
        {
            return body && body.inventory && body.inventory.GetItemCount(FFTSContent.Items.Multitool) > 0;
        }

        internal static bool TryConsumeCharge(CharacterBody body)
        {
            if (!NetworkServer.active || !HasActiveCharge(body))
                return false;

            TransformCharge(body, FFTSContent.Items.Multitool, FFTSContent.Items.MultitoolConsumed);
            return true;
        }

        internal static void RefundCharge(CharacterBody body)
        {
            TransformCharge(body, FFTSContent.Items.MultitoolConsumed, FFTSContent.Items.Multitool);
        }

        private static void TransformCharge(CharacterBody body, ItemDef from, ItemDef to)
        {
            body.inventory.RemoveItemPermanent(from, 1);
            body.inventory.GiveItem(to, 1);
            CharacterMasterNotificationQueue.SendTransformNotification(body.master, from.itemIndex, to.itemIndex,
                CharacterMasterNotificationQueue.TransformationType.Default);
        }

        private void TryRegenerateMultitool(On.RoR2.CharacterMaster.orig_OnServerStageBegin orig, RoR2.CharacterMaster self, RoR2.Stage stage)
        {
            orig(self, stage);
            if (NetworkServer.active)
            {
                int count = self.inventory.GetItemCount(FFTSContent.Items.MultitoolConsumed);
                if (count > 0)
                {
                    TransformMultitools(count, self);
                }
            }
        }
        private void TransformMultitools(int count, CharacterMaster master)
        {
            Inventory inv = master.inventory;
            inv.RemoveItemPermanent(FFTSContent.Items.MultitoolConsumed, count);
            inv.GiveItem(FFTSContent.Items.Multitool, count);

            CharacterMasterNotificationQueue.SendTransformNotification(
                master, FFTSContent.Items.MultitoolConsumed.itemIndex,
                FFTSContent.Items.Multitool.itemIndex,
                CharacterMasterNotificationQueue.TransformationType.RegeneratingScrapRegen);
        }
    }
    public class MultitoolComponent : MonoBehaviour
    {
        CharacterBody lastInteractor;

        public void SetInteractor(CharacterBody interactor, Inventory inv = null)
        {
            lastInteractor = interactor;
        }
        public bool VerifyMultitoolAndBreak()
        {
            if (Multitool.TryConsumeCharge(lastInteractor))
            {
                DestroyImmediate(this);
                return true;
            }
            return false;
        }
    }
}
