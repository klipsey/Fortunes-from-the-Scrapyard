using System;
using System.Collections;
using System.Linq;
using MSU;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using FortunesFromTheScrapyard.Unlocks.Railgunner;

namespace FortunesFromTheScrapyard
{
    internal static class RailgunnerArcherSkin
    {
        [AsyncAssetLoad]
        internal static IEnumerator Load()
        {
            var request = FFTSAssets.LoadAssetAsync<GameObject>("mdlRailgunnerArcher", FFTSBundle.Survivors);
            request.StartLoad();
            while (!request.isComplete)
                yield return null;
            if (!request.asset)
                throw new InvalidOperationException("Railgunner's Archer skin is missing. Rebuild the Fortunes asset bundles.");

            var bodyRequest = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/Railgunner/RailgunnerBody.prefab");
            while (!bodyRequest.IsDone)
                yield return null;
            var model = bodyRequest.Result.GetComponent<ModelLocator>().modelTransform.gameObject;
            var controller = model.GetComponent<ModelSkinController>();
            if (controller.skins.Any(skin => skin.name == "skinRailgunnerArcher"))
                yield break;

            var sources = request.asset.GetComponentsInChildren<MeshRenderer>();
            if (sources.Length != 4)
                throw new InvalidOperationException("Railgunner's Archer skin requires body, armor, backpack, and weapon meshes.");
            var parameters = ScriptableObject.CreateInstance<SkinDefParams>();
            parameters.name = "skinRailgunnerArcherParams";
            parameters.rendererInfos = new CharacterModel.RendererInfo[sources.Length];
            parameters.meshReplacements = new SkinDefParams.MeshReplacement[sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                var targetTransform = model.transform.Find(source.name);
                if (!targetTransform || !targetTransform.TryGetComponent(out SkinnedMeshRenderer target))
                    throw new InvalidOperationException("Railgunner's Archer skin cannot find renderer " + source.name + ".");
                var mesh = source.GetComponent<MeshFilter>().sharedMesh;
                if (!mesh || !source.sharedMaterial || mesh.bindposes.Length != target.bones.Length)
                    throw new InvalidOperationException("Railgunner's Archer skin has invalid mesh or material data for " + source.name + ".");
                parameters.rendererInfos[i] = new CharacterModel.RendererInfo
                {
                    renderer = target,
                    defaultMaterial = source.sharedMaterial,
                    defaultShadowCastingMode = ShadowCastingMode.On
                };
                parameters.meshReplacements[i] = new SkinDefParams.MeshReplacement
                {
                    renderer = target,
                    mesh = mesh
                };
            }
            string[] hiddenPaths =
            {
                "mdlRailGunBackpackScreen",
                "RailGunnerArmature/ROOT/base/stomach/backpack/StatusLED, Backpack",
                "RailGunnerArmature/ROOT/base/stomach/backpack/PowerLED, Barrel",
                "RailGunnerArmature/ROOT/base/stomach/backpack/AllBackpackVFX Root",
                "RailGunnerArmature/ROOT/base/GunRoot/GunBarrel/GunEnd/BottomRailSlider/BottomRail/StatusLED, Rail",
                "RailGunnerArmature/ROOT/base/GunRoot/GunBarrel/GunEnd/TopRailSlider/TopRail/StatusLED, Rail",
                "RailGunnerArmature/ROOT/base/GunRoot/GunBarrel/GunScope/PowerLED, Barrel"
            };
            var hiddenObjects = hiddenPaths.Select(path =>
            {
                var transform = model.transform.Find(path);
                if (!transform)
                    throw new InvalidOperationException("Railgunner's Archer skin cannot find visual effect " + path + ".");
                return transform.gameObject;
            }).ToArray();
            model.AddComponent<RailgunnerArcherVisuals>().Configure(hiddenObjects);

            var icon = Skins.CreateSkinIcon(new Color32(0x50, 0x63, 0x4A, 0xFF),
                new Color32(0x59, 0x48, 0x31, 0xFF), new Color32(0x26, 0x31, 0x2D, 0xFF),
                new Color32(0xA3, 0xA3, 0x8B, 0xFF));
            var unlockable = ScriptableObject.CreateInstance<UnlockableDef>();
            unlockable.cachedName = RailgunnerWhirlwindAchievement.UnlockableIdentifier;
            unlockable.nameToken = "FFTS_RAILGUNNER_SKIN_ARCHER_NAME";
            unlockable.achievementIcon = icon;
            FFTSContent.fftsContentPack.unlockableDefs.AddSingle(unlockable);

            var skin = ScriptableObject.CreateInstance<SkinDef>();
            skin.name = "skinRailgunnerArcher";
            skin.nameToken = "FFTS_RAILGUNNER_SKIN_ARCHER_NAME";
            skin.icon = icon;
            skin.unlockableDef = unlockable;
            skin.rootObject = model;
            skin.baseSkins = new[] { controller.skins[0] };
            skin.skinDefParams = parameters;
            if (!Skins.AddSkinToCharacter(bodyRequest.Result, skin))
                throw new InvalidOperationException("Failed to register Railgunner's Archer skin.");
        }
    }
}
