using R2API;
using RoR2;
using RoR2.ContentManagement;
using RoR2.Navigation;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace FortunesFromTheScrapyard.Items
{
    public class MultitoolDroneRemnant : NetworkBehaviour, IInteractable
    {
        private static readonly List<MultitoolDroneRemnant> instances = new List<MultitoolDroneRemnant>();

        [SyncVar] private int sourcePrefabIndex = -1;
        [SyncVar] private int masterPrefabIndex = -1;
        [SyncVar] private Vector3 visualScale = Vector3.one;
        [SyncVar] private Vector3 remnantPosition;
        [SyncVar] private bool placementReady;
        [SyncVar] private bool used;

        private int upgradeCount;
        private EquipmentIndex equipmentIndex = EquipmentIndex.None;
        private bool isTurret;
        private float placementRadius;
        private float placementRetryTimer;
        private BoxCollider interactionCollider;
        private MultitoolDroneRemnantVisual visual;

        internal static GameObject CreatePrefab()
        {
            GameObject prefab = PrefabAPI.CreateEmptyPrefab("MultitoolDroneRemnant", true);
            prefab.layer = LayerIndex.defaultLayer.intVal;
            prefab.AddComponent<BoxCollider>().isTrigger = true;
            prefab.AddComponent<EntityLocator>().entity = prefab;
            prefab.AddComponent<InteractionProcFilter>().shouldAllowOnInteractionBeginProc = false;
            prefab.AddComponent<MultitoolDroneRemnant>();
            return prefab;
        }

        internal static void Spawn(GameObject prefab, SummonMasterBehavior purchase, CharacterMaster purchasedMaster)
        {
            NetworkIdentity identity = purchase.GetComponent<NetworkIdentity>();
            int sourceIndex = FindSourcePrefabIndex(identity);
            int masterIndex = (int)MasterCatalog.FindMasterIndex(purchase.masterPrefab);
            if (sourceIndex < 0 || masterIndex < 0)
            {
                FFTSLog.Error($"Cannot create Multitool remnant for {purchase.name}: drone prefabs were not registered.");
                return;
            }

            GameObject instance = Instantiate(prefab, purchase.transform.position, purchase.transform.rotation);
            MultitoolDroneRemnant remnant = instance.GetComponent<MultitoolDroneRemnant>();
            remnant.sourcePrefabIndex = sourceIndex;
            remnant.masterPrefabIndex = masterIndex;
            remnant.visualScale = purchase.transform.localScale;
            remnant.remnantPosition = purchase.transform.position;
            remnant.isTurret = purchasedMaster.bodyPrefab.name == "Turret1Body";
            remnant.placementRadius = GetPlacementRadius(purchasedMaster);
            remnant.equipmentIndex = purchasedMaster.inventory.GetEquipmentIndex();
            ItemIndex upgradeItem = ItemCatalog.FindItemIndex("DroneUpgradeHidden");
            remnant.upgradeCount = upgradeItem != ItemIndex.None ? purchasedMaster.inventory.GetItemCount(upgradeItem) : 0;
            remnant.placementReady = !remnant.isTurret || remnant.TryPlaceTurret();
            if (!remnant.placementReady)
                FFTSLog.Warning("Multitool turret remnant is waiting for an unoccupied ground node.");
            instance.transform.position = remnant.remnantPosition;
            NetworkServer.Spawn(instance);
        }

        internal static float GetPlacementRadius(CharacterMaster master)
        {
            CharacterBody body = master.GetBody();
            float radius = Mathf.Max(1f, body ? body.radius : 1f);
            if (body && body.TryGetComponent(out Collider bodyCollider))
            {
                Vector3 extents = bodyCollider.bounds.extents;
                radius = Mathf.Max(radius, extents.x, extents.z);
            }
            return radius;
        }

        private static int FindSourcePrefabIndex(NetworkIdentity identity)
        {
            if (!identity)
                return -1;

            GameObject[] prefabs = ContentManager.networkedObjectPrefabs;
            for (int i = 0; i < prefabs.Length; i++)
            {
                if (prefabs[i].TryGetComponent(out NetworkIdentity prefabIdentity) &&
                    prefabIdentity.assetId.Equals(identity.assetId))
                    return i;
            }
            return -1;
        }

        private void OnEnable()
        {
            instances.Add(this);
            SceneCamera.onSceneCameraPreCull += OnSceneCameraPreCull;
        }

        private void OnDisable()
        {
            instances.Remove(this);
            SceneCamera.onSceneCameraPreCull -= OnSceneCameraPreCull;
        }

        private void Start()
        {
            transform.position = remnantPosition;
            interactionCollider = GetComponent<BoxCollider>();
            if (sourcePrefabIndex < 0 || sourcePrefabIndex >= ContentManager.networkedObjectPrefabs.Length)
            {
                interactionCollider.enabled = false;
                FFTSLog.Error($"Invalid Multitool remnant source prefab index {sourcePrefabIndex}.");
                return;
            }

            visual = gameObject.AddComponent<MultitoolDroneRemnantVisual>();
            visual.Initialize(ContentManager.networkedObjectPrefabs[sourcePrefabIndex], visualScale, interactionCollider);
        }

        private void Update()
        {
            transform.position = remnantPosition;
            if (interactionCollider)
                interactionCollider.enabled = placementReady && !used && visual && visual.isReady;

            if (NetworkServer.active && isTurret && !placementReady && !used)
            {
                placementRetryTimer -= Time.deltaTime;
                if (placementRetryTimer <= 0f)
                {
                    placementRetryTimer = 1f;
                    placementReady = TryPlaceTurret();
                }
            }
        }

        private void OnSceneCameraPreCull(SceneCamera camera)
        {
            if (visual)
            {
                CharacterBody viewer = camera.cameraRigController
                    ? camera.cameraRigController.localUserViewer?.cachedBody : null;
                visual.SetVisible(placementReady && !used && Multitool.HasActiveCharge(viewer));
            }
        }

        private bool TryPlaceTurret()
        {
            if (!TryFindTurretPosition(remnantPosition, placementRadius, this, out Vector3 position, out NodeGraph.NodeIndex node))
                return false;

            remnantPosition = position;
            DirectorCore.instance.AddOccupiedNode(SceneInfo.instance.groundNodes, node);
            return true;
        }

        internal static bool TryFindTurretPosition(Vector3 origin, float radius, MultitoolDroneRemnant excluded,
            out Vector3 position, out NodeGraph.NodeIndex chosenNode)
        {
            position = origin;
            chosenNode = NodeGraph.NodeIndex.invalid;
            NodeGraph graph = SceneInfo.instance ? SceneInfo.instance.groundNodes : null;
            DirectorCore director = DirectorCore.instance;
            if (!graph || !director)
                return false;

            List<NodeGraph.NodeIndex> nodes = graph.GetActiveNodesForHullMask(HullMask.Human);
            float nearestDistance = float.PositiveInfinity;
            foreach (NodeGraph.NodeIndex node in nodes)
            {
                if (!graph.GetNodePosition(node, out Vector3 candidate))
                    continue;
                float distance = (candidate - origin).sqrMagnitude;
                if (distance >= nearestDistance || distance < (radius * 2f + 0.5f) * (radius * 2f + 0.5f))
                    continue;

                bool occupied = false;
                foreach (DirectorCore.NodeReference reservation in director.occupiedNodes)
                {
                    if (reservation.nodeGraph == graph && reservation.nodeIndex == node)
                    {
                        occupied = true;
                        break;
                    }
                }
                foreach (MultitoolDroneRemnant remnant in instances)
                {
                    if (remnant == excluded || !remnant.placementReady || remnant.used)
                        continue;
                    float separation = radius + remnant.placementRadius;
                    if ((candidate - remnant.remnantPosition).sqrMagnitude < separation * separation)
                        occupied = true;
                }
                if (occupied || !IsPositionClear(candidate, radius))
                    continue;

                position = candidate;
                chosenNode = node;
                nearestDistance = distance;
            }
            return chosenNode != NodeGraph.NodeIndex.invalid;
        }

        private static bool IsPositionClear(Vector3 position, float radius)
        {
            return !Physics.CheckSphere(position + Vector3.up * (radius + 0.25f), radius,
                LayerIndex.world.mask | LayerIndex.CommonMasks.characterBodiesOrDefault | LayerIndex.CommonMasks.fakeActorLayers,
                QueryTriggerInteraction.Ignore);
        }

        public string GetContextString(Interactor activator)
        {
            return Language.GetString(FFTSContent.Items.Multitool.nameToken);
        }

        public Interactability GetInteractability(Interactor activator)
        {
            if (!placementReady || used || !visual || !visual.isReady || !activator ||
                !activator.TryGetComponent(out CharacterBody body) ||
                !Multitool.HasActiveCharge(body))
                return Interactability.Disabled;
            return Interactability.Available;
        }

        public void OnInteractionBegin(Interactor activator)
        {
            if (!NetworkServer.active || GetInteractability(activator) != Interactability.Available)
                return;

            CharacterBody body = activator.GetComponent<CharacterBody>();
            GameObject masterPrefab = MasterCatalog.GetMasterPrefab((MasterCatalog.MasterIndex)masterPrefabIndex);
            if (!masterPrefab)
            {
                FFTSLog.Error($"Invalid Multitool remnant master index {masterPrefabIndex}.");
                return;
            }

            if (isTurret && !IsPositionClear(remnantPosition, placementRadius) && !TryPlaceTurret())
            {
                FFTSLog.Warning("Multitool turret duplication needs an unoccupied ground node. No charge was consumed.");
                return;
            }

            used = true;
            if (!Multitool.TryConsumeCharge(body))
            {
                used = false;
                return;
            }

            CharacterMaster spawnedMaster = null;
            try
            {
                spawnedMaster = new MasterSummon
                {
                    masterPrefab = masterPrefab,
                    position = remnantPosition,
                    rotation = transform.rotation,
                    summonerBodyObject = body.gameObject,
                    ignoreTeamMemberLimit = true,
                    useAmbientLevel = true,
                    preSpawnSetupCallback = SetupInventory
                }.Perform();
                if (!spawnedMaster)
                {
                    FFTSLog.Error("Multitool could not summon the duplicate drone. The charge was refunded.");
                    return;
                }
                NetworkServer.Destroy(gameObject);
            }
            finally
            {
                if (!spawnedMaster)
                {
                    Multitool.RefundCharge(body);
                    used = false;
                }
            }
        }

        private void SetupInventory(CharacterMaster master)
        {
            SetupInventory(master, upgradeCount, equipmentIndex);
        }

        internal static void SetupInventory(CharacterMaster master, int upgradeCount, EquipmentIndex equipmentIndex)
        {
            ItemIndex upgradeItem = ItemCatalog.FindItemIndex("DroneUpgradeHidden");
            if (upgradeCount > 0 && upgradeItem != ItemIndex.None)
                master.inventory.GiveItem(upgradeItem, upgradeCount);
            master.inventory.SetEquipmentIndex(equipmentIndex);
        }

        public bool ShouldIgnoreSpherecastForInteractibility(Interactor activator) => false;
        public bool ShouldShowOnScanner() => false;
        public bool ShouldProximityHighlight() => false;
    }
}
