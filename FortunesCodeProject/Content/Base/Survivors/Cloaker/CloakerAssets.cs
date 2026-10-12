using System;
using System.Collections.Generic;
using System.Linq;
using MSU;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace FortunesFromTheScrapyard.Survivors.Cloaker
{
    internal static class CloakerAssets
    {
        internal const string ChargeSound = "Play_sfx_cloaker_charge";
        internal const string ChargedShotSound = "Play_sfx_cloaker_chargeshot";
        internal const string ShotSound = "Play_sfx_cloaker_shot";
        internal const string PingSound = "Play_sfx_cloaker_ping";
        internal const string ScreamSound = "Play_sfx_cloaker_scream";
        internal const string DashSound = "Play_drifter_repossess_dash";
        internal static NetworkSoundEventDef StealthSound;
        internal static GameObject RailTracer, RailImpact, GoldTracer, CritTracer;
        internal static GameObject PistolMuzzle, PistolImpact;
        internal static GameObject RangeIndicator, TrackingIndicator, ConsumeEffect, SuccessfulConsumeEffect, ScreechEffect;

        internal static void Initialize(SurvivorAssetCollection collection)
        {
            StealthSound = FFTSContent.CreateAndAddNetworkSoundEventDef("Play_sfx_cloaker_stealth");
            GoldTracer = LoadAddress<GameObject>("RoR2/Base/GoldGat/TracerGoldGat.prefab");
            CritTracer = LoadAddress<GameObject>("RoR2/Base/Captain/TracerCaptainShotgun.prefab");
            PistolMuzzle = LoadAddress<GameObject>("RoR2/Base/Common/VFX/Muzzleflash1.prefab");
            PistolImpact = LoadAddress<GameObject>("RoR2/Base/Commando/HitsparkCommando.prefab");
            RailTracer = LoadAddress<GameObject>("RoR2/DLC1/Railgunner/TracerRailgun.prefab");
            RailImpact = LoadAddress<GameObject>("RoR2/DLC1/Railgunner/ImpactRailgun.prefab");
            RangeIndicator = collection.FindAsset<GameObject>("CloakerRangeIndicator");
            TrackingIndicator = collection.FindAsset<GameObject>("CloakerTrackingIndicator");
            ConsumeEffect = collection.FindAsset<GameObject>("CloakerMarkedConsumeEffect");
            ScreechEffect = collection.FindAsset<GameObject>("CloakerScreechEffect");
            SuccessfulConsumeEffect = collection.FindAsset<GameObject>("CloakerMarkedSuccessfulConsumeEffect");
            ConfigureMarkConsumptionMaterials(ConsumeEffect);
            ConfigureMarkConsumptionMaterials(SuccessfulConsumeEffect);
            SuccessfulConsumeEffect = CreateMarkKillEffect(SuccessfulConsumeEffect);
            BuffDef markedBuff = collection.FindAsset<BuffDef>("bdCloakerMarked");
            Material markOverlayMaterial = collection.FindAsset<Material>("matCloakerMark");
            if (!markedBuff || !markOverlayMaterial)
                throw new InvalidOperationException("Cloaker's marked buff or overlay material is missing from acCloaker.");
            if (BuffOverlays.dictionaryCreated)
                throw new InvalidOperationException("Cloaker's mark overlay must be registered before MSU initializes buff overlays.");
            BuffOverlays.AddBuffOverlay(markedBuff, markOverlayMaterial);
            BuffDef akimboSpeed = ScriptableObject.CreateInstance<BuffDef>();
            akimboSpeed.name = "bdCloakerAkimboSpeed";
            akimboSpeed.buffColor = new Color(0.75f, 0.33f, 1f);
            akimboSpeed.iconSprite = markedBuff.iconSprite;
            FFTSContent.fftsContentPack.buffDefs.AddSingle(akimboSpeed);
            FFTSContent.Buffs.bdCloakerAkimboSpeed = akimboSpeed;
            GameObject mark = collection.FindAsset<GameObject>("CloakerMarkEffect");
            if (!TempVisualEffectAPI.AddTemporaryVisualEffect(mark,
                body => body.HasBuff(FFTSContent.Buffs.bdCloakerMarked), true))
                throw new InvalidOperationException("Failed to register Cloaker's marked visual effect.");

            GameObject vanilla = LoadAddress<GameObject>("RoR2/Base/NearbyDamageBonus/NearbyDamageBonusIndicator.prefab");
            AssignMaterial("Donut", "Donut");
            AssignMaterial("Radius", "Radius, Spherical");
            void AssignMaterial(string target, string source)
            {
                Material material = new Material(vanilla.transform.Find(source).GetComponent<MeshRenderer>().sharedMaterial);
                material.SetColor("_TintColor", new Color(0.2358491f, 0.1768868f, 0.2268582f));
                RangeIndicator.transform.Find(target).GetComponent<MeshRenderer>().sharedMaterial = material;
            }
        }

        private static GameObject CreateMarkKillEffect(GameObject original)
        {
            ParticleSystemRenderer[] originalRenderers = original.GetComponentsInChildren<ParticleSystemRenderer>(true);
            Material palette = originalRenderers.Select(renderer => renderer.sharedMaterial)
                .FirstOrDefault(material => material && material.name == "matCloakerHitspark");
            ParticleSystemRenderer[] dollarSigns = originalRenderers.Where(renderer => renderer.sharedMaterial
                && renderer.sharedMaterial.name == "matCloakerSuccessfulMark").ToArray();
            if (!palette || dollarSigns.Length == 0)
                throw new InvalidOperationException("Cloaker's mark kill effect is missing its palette or dollar-sign particles.");

            GameObject banditEffect = LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/ImpactEffects/Bandit2ResetEffect");
            if (!banditEffect)
                throw new InvalidOperationException("Bandit's Lights Out kill effect failed to load.");

            GameObject effect = banditEffect.InstantiateClone("CloakerMarkKillEffect", false);
            Color tint = palette.GetColor("_TintColor");
            Texture ramp = palette.GetTexture("_RemapTex");
            if (!ramp)
                throw new InvalidOperationException("Cloaker's mark kill effect palette is missing its color ramp.");

            foreach (ParticleSystemRenderer renderer in effect.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (!materials[i]) continue;
                    Material material = new Material(materials[i]);
                    material.name = $"matCloakerMarkKill_{materials[i].name}";
                    if (material.HasProperty("_RemapTex"))
                        material.SetTexture("_RemapTex", ramp);
                    if (material.HasProperty("_TintColor"))
                        material.SetColor("_TintColor", tint);
                    if (material.HasProperty("_EmissionColor"))
                        material.SetColor("_EmissionColor", tint);
                    materials[i] = material;
                }
                renderer.sharedMaterials = materials;
            }
            foreach (Light light in effect.GetComponentsInChildren<Light>(true))
                light.color = tint;

            Material dollarMaterial = new Material(dollarSigns[0].sharedMaterial);
            dollarMaterial.name = "matCloakerMarkKillDollar";
            dollarMaterial.SetTexture("_RemapTex", LoadAddress<Texture2D>("RoR2/DLC1/Common/ColorRamps/texRampGold.png"));
            dollarMaterial.SetColor("_TintColor", new Color(1f, 0.82f, 0.2f, 1f));
            foreach (ParticleSystemRenderer dollarSign in dollarSigns)
            {
                GameObject dollarSignEffect = UnityEngine.Object.Instantiate(dollarSign.gameObject, effect.transform, false);
                dollarSignEffect.GetComponent<ParticleSystemRenderer>().sharedMaterial = dollarMaterial;
                ParticleSystem.MainModule particles = dollarSignEffect.GetComponent<ParticleSystem>().main;
                particles.startSizeMultiplier *= 2.5f;
                particles.startSpeedMultiplier *= 3f;
                particles.startLifetimeMultiplier *= 4f;
                particles.startColor = Color.white;
                ParticleSystem.VelocityOverLifetimeModule velocity = dollarSignEffect.GetComponent<ParticleSystem>().velocityOverLifetime;
                velocity.enabled = true;
                velocity.x = velocity.y = velocity.z = new ParticleSystem.MinMaxCurve(0f);
                // (1-t)^3 averages 1/4, keeping travel distance the same over 4x lifetime.
                velocity.speedModifier = new ParticleSystem.MinMaxCurve(1f,
                    new AnimationCurve(new Keyframe(0f, 1f, -3f, -3f), new Keyframe(1f, 0f, 0f, 0f)));
            }
            effect.GetComponent<EffectComponent>().soundName = original.GetComponent<EffectComponent>().soundName;
            FFTSContent.CreateAndAddEffectDef(effect);
            return effect;
        }

        private static void ConfigureMarkConsumptionMaterials(GameObject effect)
        {
            if (!effect)
                throw new InvalidOperationException("Cloaker's mark consumption effect is missing.");

            foreach (ParticleSystemRenderer renderer in effect.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                Material material = renderer.sharedMaterial;
                if (!material || (material.name != "matCloakerMarkBackdrop"
                    && material.name != "matCloakerSuccessfulMark" && material.name != "matCloakerHitspark"))
                    continue;

                material.SetFloat("_CloudsOn", 0f);
                material.DisableKeyword("USE_CLOUDS");
                if (material.name == "matCloakerMarkBackdrop")
                {
                    material.SetFloat("_DisableRemapOn", 1f);
                    material.EnableKeyword("DISABLEREMAP");
                    material.SetFloat("_CalcTextureAlphaOn", 1f);
                    material.EnableKeyword("CALCTEXTUREALPHA");
                }
                material.SetFloat("_VertexColorOn", 1f);
                material.EnableKeyword("VERTEXCOLOR");
                material.SetFloat("_DepthOffset", 0f);
                material.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
                material.SetFloat("_ZWrite", 0f);
            }
        }

        internal static T LoadAddress<T>(string key) where T : UnityEngine.Object
        {
            AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(key);
            T asset = handle.WaitForCompletion();
            if (handle.Status != AsyncOperationStatus.Succeeded || !asset)
                throw new InvalidOperationException($"Required Cloaker Addressable '{key}' failed to load.", handle.OperationException);
            return asset;
        }

        internal static void ConfigureAudio(GameObject body)
        {
            if (Application.isBatchMode) return;
            AkGameObj emitter = body.GetComponent<AkGameObj>() ?? body.AddComponent<AkGameObj>();
            emitter.isStaticObject = false;
            emitter.enabled = true;
            CopyBanks("RoR2/Base/Bandit2/Bandit2Body.prefab");
            CopyBanks("RoR2/Base/Huntress/HuntressBody.prefab");
            CopyBanks("RoR2/DLC3/Drifter/DrifterBody.prefab");
            void CopyBanks(string key)
            {
                AkBank[] sources = LoadAddress<GameObject>(key).GetComponents<AkBank>();
                if (sources.Length == 0)
                    throw new InvalidOperationException($"Cloaker audio donor '{key}' has no banks.");
                foreach (AkBank source in sources)
                {
                    if (source.data == null || !source.data.IsValid() || source.triggerList == null || source.unloadTriggerList == null)
                        throw new InvalidOperationException($"Cloaker audio donor '{key}' has an incomplete bank.");
                    AkBank bank = null;
                    foreach (AkBank existing in body.GetComponents<AkBank>())
                        if (existing.data != null && existing.data.IsValid() && existing.data.Name == source.data.Name)
                        {
                            bank = existing;
                            break;
                        }
                    if (!bank) bank = body.AddComponent<AkBank>();
                    bank.data = new AK.Wwise.Bank { WwiseObjectReference = source.data.WwiseObjectReference };
                    bank.triggerList = new List<int>(source.triggerList);
                    bank.unloadTriggerList = new List<int>(source.unloadTriggerList);
                    bank.useOtherObject = false;
                    bank.decodeBank = source.decodeBank;
                    bank.saveDecodedBank = source.saveDecodedBank;
                    bank.overrideLoadSetting = true;
                    bank.loadAsynchronous = false;
                    bank.enabled = true;
                }
            }
        }
    }
}
