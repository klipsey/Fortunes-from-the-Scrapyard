using System;
using System.Collections.Generic;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using RoR2.ContentManagement;
using MSU.Config;
using RoR2.Items;
using RoR2.Projectile;
using MSU;

namespace FortunesFromTheScrapyard.Items
{
    public class LethalInjection : FFTSItem
    {
        public const string TOKEN = "FFTS_ITEM_LETHALINJECTION_DESCRIPTION";

        [FFTSConfigureField(FFTSConfig.ID_ITEMS, 0f, 100f, configDescOverride = "Percent chance to inflict Lethal Injection, regardless of item count. Scales with the hit's proc coefficient.")]
        [FormatToken(TOKEN, 0)]
        public static float procChanceBase = 15f;

        [FFTSConfigureField(FFTSConfig.ID_ITEMS, 0f, 30f, configDescOverride = "Debuff duration in seconds. Scales with proc coefficient; reapplications refresh the single debuff without shortening its remaining duration.")]
        [FormatToken(TOKEN, 1)]
        public static float buffDuration = 3f;

        [FFTSConfigureField(FFTSConfig.ID_ITEMS, 0f, 1f, configDescOverride = "Fraction of the victim's maximum health added permanently to its execute threshold per second while injected. The full buildup rate is multiplied by one plus the number of other unique active DOT types. 0.01 is 1%.")]
        [FormatToken(TOKEN, FormatTokenAttribute.OperationTypeEnum.MultiplyByN, 100, 2)]
        public static float executePercentPerSecond = 0.01f;

        [FFTSConfigureField(FFTSConfig.ID_ITEMS, 0f, 1f, configDescOverride = "Additional fraction of maximum health added to execute buildup per second for each extra item held by the latest applier, before unique DOT scaling. 0.005 is 0.5%.")]
        [FormatToken(TOKEN, FormatTokenAttribute.OperationTypeEnum.MultiplyByN, 100, 3)]
        public static float executePercentPerSecondStack = 0.005f;

        [FormatToken(TOKEN, 4)]
        private const float legacyDotDamageMultiplier = 1f;

        public static GameObject lethalInjectionPrefab;
        private static GameObject injectionTrackerPrefab;
        internal static readonly Dictionary<CharacterBody, LethalInjectionTracker> injections = new Dictionary<CharacterBody, LethalInjectionTracker>();

        public override void Initialize()
        {
            lethalInjectionPrefab = assetCollection.FindAsset<GameObject>("LethalInjectionPrefab");
            assetCollection.FindAsset<BuffDef>("bdLethalInjection").canStack = false;

            injectionTrackerPrefab = PrefabAPI.CreateEmptyPrefab("LethalInjectionTracker", true);
            injectionTrackerPrefab.AddComponent<NetworkedBodyAttachment>().forceHostAuthority = true;
            injectionTrackerPrefab.AddComponent<LethalInjectionTracker>();

            On.RoR2.CharacterBody.UpdateBuffs += CharacterBody_UpdateBuffs;
            ExecuteAPI.CalculateExecuteThreshold += CalculateExecuteThreshold;
            GlobalEventManager.onServerCharacterExecuted += OnServerCharacterExecuted;
        }

        public override void ModifyContentPack(ContentPack contentPack)
        {
            base.ModifyContentPack(contentPack);
            contentPack.networkedObjectPrefabs.AddSingle(injectionTrackerPrefab);
        }

        private void CharacterBody_UpdateBuffs(On.RoR2.CharacterBody.orig_UpdateBuffs orig, CharacterBody self, float deltaTime)
        {
            if (NetworkServer.active && injections.TryGetValue(self, out var injection))
            {
                injection.UpdateExecuteThreshold(deltaTime);
            }
            orig(self, deltaTime);
        }

        private static void CalculateExecuteThreshold(CharacterBody victimBody, ref float highestExecuteThreshold)
        {
            if (victimBody && victimBody.healthComponent)
            {
                highestExecuteThreshold = Mathf.Max(highestExecuteThreshold, GetExecuteFraction(victimBody.healthComponent));
            }
        }

        internal static float GetExecuteFraction(HealthComponent healthComponent)
        {
            if (!healthComponent.body ||
                (healthComponent.body.bodyFlags & CharacterBody.BodyFlags.ImmuneToExecutes) != 0 ||
                !injections.TryGetValue(healthComponent.body, out var injection))
            {
                return 0f;
            }
            return injection.injectionExecuteThreshold * healthComponent.fullHealth / healthComponent.fullCombinedHealth;
        }

        private static void OnServerCharacterExecuted(DamageReport damageReport, float executionHealthLost)
        {
            HealthComponent victimHealth = damageReport.victim;
            if (!victimHealth || !damageReport.victimBody || executionHealthLost <= 0f ||
                executionHealthLost > GetExecuteFraction(victimHealth) * victimHealth.fullCombinedHealth)
            {
                return;
            }

            EffectManager.SpawnEffect(HealthComponent.AssetReferences.permanentDebuffEffectPrefab, new EffectData
            {
                origin = damageReport.victimBody.corePosition,
                scale = damageReport.victimBody.radius
            }, transmit: true);
        }

        public override bool IsAvailable(ContentPack contentPack)
        {
            return true;
        }

        public override FFTSAssetRequest LoadAssetRequest()
        {
            return FFTSAssets.LoadAssetAsync<ItemAssetCollection>("acLethalInjection", FFTSBundle.Items);
        }

        public class LethalInjectionBehaviour : BaseItemBodyBehavior, IOnDamageDealtServerReceiver
        {
            [ItemDefAssociation]
            public static ItemDef GetItemDef() => FFTSContent.Items.LethalInjection;

            public void OnDamageDealtServer(DamageReport damageReport)
            {
                DamageInfo damageInfo = damageReport.damageInfo;
                CharacterBody victimBody = damageReport.victimBody;
                HealthComponent victimHealth = damageReport.victim;
                if (!NetworkServer.active || stack <= 0 || !body || !victimBody || !victimHealth || !victimHealth.alive ||
                    damageInfo.rejected || damageInfo.procCoefficient <= 0f || buffDuration <= 0f ||
                    !Util.CheckRoll(procChanceBase * damageInfo.procCoefficient, body.master))
                {
                    return;
                }

                BuffDef debuff = FFTSContent.Buffs.bdLethalInjection;
                float duration = buffDuration * damageInfo.procCoefficient;
                victimBody.AddTimedBuff(debuff, duration);

                CharacterBody.TimedBuff injectionBuff = null;
                foreach (CharacterBody.TimedBuff timedBuff in victimBody.timedBuffs)
                {
                    if (timedBuff.buffIndex == debuff.buffIndex)
                    {
                        injectionBuff = timedBuff;
                        break;
                    }
                }
                if (injectionBuff == null || !victimBody.HasBuff(debuff))
                {
                    return;
                }

                if (!injections.TryGetValue(victimBody, out var injection))
                {
                    GameObject tracker = Instantiate(injectionTrackerPrefab);
                    injection = tracker.GetComponent<LethalInjectionTracker>();
                    tracker.GetComponent<NetworkedBodyAttachment>().AttachToGameObjectAndSpawn(victimBody.gameObject);
                }
                injection.ApplyInjection(injectionBuff,
                    GetStackValue(executePercentPerSecond, executePercentPerSecondStack, stack));
                injection.attackerObject = damageInfo.attacker;

                Vector3 position = damageInfo.position;
                Vector3 forward = victimBody.corePosition - position;
                ProjectileManager.instance.FireProjectile(lethalInjectionPrefab, position, Util.QuaternionSafeLookRotation(forward),
                    damageInfo.attacker, 0f, 100f, false, DamageColorIndex.Default, null, forward.magnitude * 5f);
            }
        }

    }

    public class LethalInjectionTracker : NetworkBehaviour, INetworkedBodyAttachmentListener
    {
        private CharacterBody hostBody;
        private CharacterBody.TimedBuff activeInjection;
        private float executeRate;
        private readonly HashSet<DotController.DotIndex> activeDotTypes = new HashSet<DotController.DotIndex>();
        internal GameObject attackerObject;

        [SyncVar]
        public float injectionExecuteThreshold;

        public void OnAttachedBodyDiscovered(NetworkedBodyAttachment networkedBodyAttachment, CharacterBody attachedBody)
        {
            hostBody = attachedBody;
            LethalInjection.injections.Add(hostBody, this);
        }

        internal void ApplyInjection(CharacterBody.TimedBuff buff, float executeRate)
        {
            activeInjection = buff;
            this.executeRate = executeRate;
        }

        public void UpdateExecuteThreshold(float deltaTime)
        {
            HealthComponent healthComponent = hostBody.healthComponent;
            if (!healthComponent || !healthComponent.alive)
            {
                return;
            }

            if (activeInjection != null)
            {
                if (!hostBody.HasBuff(FFTSContent.Buffs.bdLethalInjection) ||
                    !hostBody.timedBuffs.Contains(activeInjection) ||
                    activeInjection.buffIndex != FFTSContent.Buffs.bdLethalInjection.buffIndex)
                {
                    activeInjection = null;
                }
                else
                {
                    activeDotTypes.Clear();
                    DotController dotController = DotController.FindDotController(hostBody.gameObject);
                    if (dotController)
                    {
                        foreach (DotController.DotStack dotStack in dotController.dotStackList)
                        {
                            if (dotStack.dotIndex != DotController.DotIndex.None && dotStack.timer > 0f)
                            {
                                activeDotTypes.Add(dotStack.dotIndex);
                            }
                        }
                    }

                    injectionExecuteThreshold += executeRate * (1 + activeDotTypes.Count) * Mathf.Clamp(activeInjection.timer, 0f, deltaTime);
                    if (activeInjection.timer <= deltaTime)
                        activeInjection = null;
                }
            }

            float executeFraction = LethalInjection.GetExecuteFraction(healthComponent);
            if (executeFraction > 0f && healthComponent.combinedHealthFraction <= executeFraction &&
                !healthComponent.godMode &&
                !hostBody.HasBuff(RoR2Content.Buffs.HiddenInvincibility) && !hostBody.HasBuff(RoR2Content.Buffs.Immune) &&
                !hostBody.HasBuff(DLC2Content.Buffs.SoulSurge))
            {
                healthComponent.TakeDamage(new DamageInfo
                {
                    attacker = attackerObject,
                    damage = 0f,
                    procCoefficient = 0f,
                    position = hostBody.corePosition,
                    damageType = DamageType.BypassBlock | DamageType.Silent
                });
            }
        }

        private void OnDestroy()
        {
            if (!ReferenceEquals(hostBody, null) && LethalInjection.injections.TryGetValue(hostBody, out var injection) && injection == this)
            {
                LethalInjection.injections.Remove(hostBody);
            }
        }
    }
}
