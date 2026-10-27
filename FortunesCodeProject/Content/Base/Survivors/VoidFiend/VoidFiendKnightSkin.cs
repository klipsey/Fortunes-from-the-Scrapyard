using System;
using System.Collections;
using System.Linq;
using FortunesFromTheScrapyard.Unlocks.VoidFiend;
using MSU;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;

namespace FortunesFromTheScrapyard
{
    internal static class VoidFiendKnightSkin
    {
        [AsyncAssetLoad]
        internal static IEnumerator Load()
        {
            var request = FFTSAssets.LoadAssetAsync<GameObject>("mdlVoidFiendKnight", FFTSBundle.Survivors);
            request.StartLoad();
            while (!request.isComplete)
                yield return null;
            if (!request.asset)
                throw new InvalidOperationException("Void Fiend's White Knight skin is missing. Rebuild the Fortunes asset bundles.");

            var bodyRequest = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/VoidSurvivor/VoidSurvivorBody.prefab");
            while (!bodyRequest.IsDone)
                yield return null;
            var model = bodyRequest.Result.GetComponent<ModelLocator>().modelTransform.gameObject;
            var controller = model.GetComponent<ModelSkinController>();
            if (controller.skins.Any(skin => skin.name == "skinVoidFiendKnight"))
                yield break;

            string[] names = { "mdlVoidSurvivorBody", "mdlVoidSurvivorHead", "mdlVoidSurvivorMetal", "metalcollar.001" };
            // Body and head are the armor shell; Metal and collar host the inside crack meshes so only they receive overlays like corruption.
            var sources = request.asset.GetComponentsInChildren<MeshRenderer>();
            if (sources.Length != names.Length)
                throw new InvalidOperationException("Void Fiend's White Knight skin requires armor and inside meshes for the body and head.");
            var parameters = ScriptableObject.CreateInstance<SkinDefParams>();
            parameters.name = "skinVoidFiendKnightParams";
            parameters.rendererInfos = new CharacterModel.RendererInfo[names.Length];
            parameters.meshReplacements = new SkinDefParams.MeshReplacement[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var transform = model.transform.Find(names[i]);
                if (!transform || !transform.TryGetComponent(out SkinnedMeshRenderer target))
                    throw new InvalidOperationException("Void Fiend's White Knight skin cannot find renderer " + names[i] + ".");
                var source = sources.SingleOrDefault(renderer => renderer.name == names[i]);
                if (!source || !source.TryGetComponent(out MeshFilter filter) || !filter.sharedMesh
                    || !source.sharedMaterial || filter.sharedMesh.bindposes.Length != target.bones.Length)
                    throw new InvalidOperationException("Void Fiend's White Knight skin has invalid mesh or material data for " + names[i] + ".");
                parameters.rendererInfos[i] = new CharacterModel.RendererInfo
                {
                    renderer = target,
                    defaultMaterial = source.sharedMaterial,
                    defaultShadowCastingMode = ShadowCastingMode.On,
                    ignoreOverlays = i < 2
                };
                parameters.meshReplacements[i] = new SkinDefParams.MeshReplacement { renderer = target, mesh = filter.sharedMesh };
            }

            var icon = Skins.CreateSkinIcon(new Color32(0xE1, 0xE1, 0xDD, 0xFF),
                new Color32(0x7C, 0x7E, 0x82, 0xFF), new Color32(0x29, 0x2A, 0x30, 0xFF),
                new Color32(0x88, 0x25, 0x2C, 0xFF));
            var unlockable = ScriptableObject.CreateInstance<UnlockableDef>();
            unlockable.cachedName = VoidFiendWhirlwindAchievement.UnlockableIdentifier;
            unlockable.nameToken = "FFTS_VOIDFIEND_SKIN_KNIGHT_NAME";
            unlockable.achievementIcon = icon;
            FFTSContent.fftsContentPack.unlockableDefs.AddSingle(unlockable);

            var skin = ScriptableObject.CreateInstance<SkinDef>();
            skin.name = "skinVoidFiendKnight";
            skin.nameToken = "FFTS_VOIDFIEND_SKIN_KNIGHT_NAME";
            skin.icon = icon;
            skin.unlockableDef = unlockable;
            skin.rootObject = model;
            skin.baseSkins = new[] { controller.skins[0] };
            skin.skinDefParams = parameters;
            if (!Skins.AddSkinToCharacter(bodyRequest.Result, skin))
                throw new InvalidOperationException("Failed to register Void Fiend's White Knight skin.");
        }
    }
}
