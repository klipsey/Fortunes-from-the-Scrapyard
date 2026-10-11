using RoR2;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace FortunesFromTheScrapyard.Items
{
    public class MultitoolDroneRemnantVisual : MonoBehaviour
    {
        private GameObject visualRoot;
        private TextMeshPro label;
        private Material fadedMaterial;
        private Highlight highlight;
        internal bool isReady { get; private set; }

        internal void Initialize(GameObject sourcePrefab, Vector3 scale, BoxCollider interactionCollider)
        {
            Material ghostMaterial = LegacyResourcesAPI.Load<Material>("Materials/matGhostEffect");
            GameObject ringPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/LowerPricedChestsGlow");
            GameObject hologramPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/CostHologramContent");
            TextMeshPro textTemplate = hologramPrefab ? hologramPrefab.GetComponentInChildren<TextMeshPro>() : null;
            if (!ghostMaterial || !ringPrefab || !textTemplate)
            {
                interactionCollider.enabled = false;
                FFTSLog.Error("Multitool remnant visual assets were not found.");
                return;
            }

            fadedMaterial = new Material(ghostMaterial);
            fadedMaterial.SetFloat("_ExternalAlpha", 0.35f);
            fadedMaterial.SetFloat("_ZWrite", 0f);
            fadedMaterial.SetColor("_TintColor", new Color(0.6f, 0.85f, 1f, 0.35f));
            visualRoot = new GameObject("MultitoolRemnantVisual");
            visualRoot.transform.SetParent(transform, false);

            Dictionary<Transform, Transform> transforms = new Dictionary<Transform, Transform>();
            CopyTransforms(sourcePrefab.transform, visualRoot.transform, transforms);
            Transform modelRoot = transforms[sourcePrefab.transform];
            modelRoot.localPosition = Vector3.zero;
            modelRoot.localRotation = Quaternion.identity;
            modelRoot.localScale = scale;
            List<Renderer> renderers = CopyRenderers(sourcePrefab, transforms);
            if (renderers.Count == 0)
            {
                interactionCollider.enabled = false;
                FFTSLog.Error($"Multitool remnant prefab {sourcePrefab.name} has no model renderers.");
                visualRoot.SetActive(false);
                return;
            }

            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            interactionCollider.center = transform.InverseTransformPoint(bounds.center);
            interactionCollider.size = bounds.size + Vector3.one * 0.25f;

            highlight = gameObject.AddComponent<Highlight>();
            highlight.highlightColor = Highlight.HighlightColor.interactive;
            highlight.targetRenderer = renderers[0];
            highlight.enabled = false;

            GameObject ring = Instantiate(ringPrefab, visualRoot.transform);
            ring.transform.localPosition = Vector3.zero;
            ring.transform.localRotation = Quaternion.identity;
            float ringScale = Mathf.Max(1f, Mathf.Max(bounds.extents.x, bounds.extents.z));
            ring.transform.localScale = Vector3.one * ringScale;

            GameObject labelObject = new GameObject("MultitoolLabel");
            // Keep Billboard active when per-camera visibility hides the text.
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localPosition = transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.max.y + 0.75f, bounds.center.z));
            labelObject.transform.localScale = Vector3.one * 0.1f;
            label = labelObject.AddComponent<TextMeshPro>();
            label.font = textTemplate.font;
            label.fontSharedMaterial = textTemplate.fontSharedMaterial;
            label.fontSize = 24f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = ColorCatalog.GetColor(ColorCatalog.ColorIndex.Interactable);
            label.text = Language.GetString(FFTSContent.Items.Multitool.nameToken);
            label.rectTransform.sizeDelta = new Vector2(60f, 10f);
            label.enabled = false;
            labelObject.AddComponent<Billboard>();
            visualRoot.SetActive(false);
            isReady = true;
        }

        private static void CopyTransforms(Transform source, Transform parent, Dictionary<Transform, Transform> transforms)
        {
            GameObject copy = new GameObject(source.name);
            copy.layer = LayerIndex.noCollision.intVal;
            copy.transform.SetParent(parent, false);
            copy.transform.localPosition = source.localPosition;
            copy.transform.localRotation = source.localRotation;
            copy.transform.localScale = source.localScale;
            copy.SetActive(source.gameObject.activeSelf);
            transforms.Add(source, copy.transform);
            foreach (Transform child in source)
                CopyTransforms(child, copy.transform, transforms);
        }

        private List<Renderer> CopyRenderers(GameObject sourcePrefab, Dictionary<Transform, Transform> transforms)
        {
            List<Renderer> result = new List<Renderer>();
            foreach (Renderer source in sourcePrefab.GetComponentsInChildren<Renderer>())
            {
                if (!source.enabled)
                    continue;

                GameObject copy = transforms[source.transform].gameObject;
                Renderer renderer;
                if (source is SkinnedMeshRenderer skinned && skinned.sharedMesh)
                {
                    SkinnedMeshRenderer mesh = copy.AddComponent<SkinnedMeshRenderer>();
                    mesh.sharedMesh = skinned.sharedMesh;
                    mesh.localBounds = skinned.localBounds;
                    mesh.updateWhenOffscreen = true;
                    Transform[] bones = new Transform[skinned.bones.Length];
                    for (int i = 0; i < bones.Length; i++)
                        if (skinned.bones[i] && transforms.TryGetValue(skinned.bones[i], out Transform bone))
                            bones[i] = bone;
                    mesh.bones = bones;
                    if (skinned.rootBone && transforms.TryGetValue(skinned.rootBone, out Transform rootBone))
                        mesh.rootBone = rootBone;
                    renderer = mesh;
                }
                else if (source is MeshRenderer && source.TryGetComponent(out MeshFilter filter) && filter.sharedMesh)
                {
                    copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    renderer = copy.AddComponent<MeshRenderer>();
                }
                else
                {
                    continue;
                }

                Material[] materials = new Material[source.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = fadedMaterial;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                result.Add(renderer);
            }
            return result;
        }

        internal void SetVisible(bool visible)
        {
            if (!visualRoot || !label)
                return;
            visualRoot.SetActive(visible);
            highlight.enabled = visible;
            label.enabled = visible;
        }

        private void OnDestroy()
        {
            if (fadedMaterial)
                Destroy(fadedMaterial);
        }
    }
}
