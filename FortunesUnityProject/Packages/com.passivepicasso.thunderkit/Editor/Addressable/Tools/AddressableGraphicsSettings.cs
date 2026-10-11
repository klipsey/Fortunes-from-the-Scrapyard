#if TK_ADDRESSABLE
using System;
using System.Collections.Generic;
using System.IO;
using ThunderKit.Core.Data;
using ThunderKit.Pipelines.Jobs;
using UnityEditor;
using UnityEditor.Build.Content;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.Rendering;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.UIElements;

namespace ThunderKit.Addressable.Tools
{
    public class AddressableGraphicsSettings : ThunderKitSetting
    {
        public string CustomDeferredReflection;
        public string CustomDeferredShading;
        public string CustomDeferredScreenspaceShadows;

        public static event EventHandler AddressablesInitialized;

        private static readonly Dictionary<BuiltinShaderType, (string address, AsyncOperationHandle<Shader> handle)> shaderLoads
            = new Dictionary<BuiltinShaderType, (string address, AsyncOperationHandle<Shader> handle)>();
        private static IResourceLocator gameLocator;
        private static int catalogVersion;
        private static string providerSuffix;

        [InitializeOnLoadMethod]
        public static void OnLoad()
        {
            CompilationPipeline.compilationStarted -= ClearSelectionIfUnsavable;
            CompilationPipeline.compilationStarted += ClearSelectionIfUnsavable;
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                UnsetAllShaders();
                AssetBundle.UnloadAllAssetBundles(true);
            };

            EditorApplication.delayCall += InitializeAddressables;
        }

        static string RedirectInternalIdsToGameDirectory(IResourceLocation location)
        {
            switch (location.ResourceType)
            {
                case var _ when location.InternalId.Equals("Library/com.unity.addressables/aa/Windows/settings.json"):
                    return "Assets/StreamingAssets/aa/settings.json";
                case var _ when location.InternalId.Equals("Library/com.unity.addressables/aa/Windows/catalog.json"):
                    return "Assets/StreamingAssets/aa/catalog.json";
                case var _ when location.InternalId.StartsWith("Library/com.unity.addressables/aa/Windows"):
                    {
                        var iid = location.InternalId;
                        var path = iid.Replace("Library/com.unity.addressables/aa/Windows", ThunderKitSettings.EditTimePath);
                        return path;
                    }
                case var f when f == typeof(IAssetBundleResource):
                    {
                        var iid = location.InternalId;
                        var aaIndex = iid.IndexOf("/aa");
                        if (aaIndex >= 0)
                        {
                            var start = aaIndex + 4;
                            var path = iid.Substring(start);
                            return Path.Combine(ThunderKitSettings.EditTimePath, path);
                        }
                        return iid;
                    }
                default:
                    var result = location.InternalId;
                    return result;
            }
        }

        static void InitializeAddressables()
        {
            // Other editor initializers may already own bundles through Addressables' cached operations.
            Addressables.InternalIdTransformFunc = RedirectInternalIdsToGameDirectory;
            var aop = Addressables.InitializeAsync();
            gameLocator = aop.WaitForCompletion();
            AssignShaders();
        }

        public static void ReloadAddressables()
        {
            var settings = GetOrCreateSettings<ThunderKitSettings>();
            // Unity unloads Play Mode bundles without clearing their cached Addressables operations.
            string nextSuffix = "ThunderKitEditor" + ++catalogVersion;
            var handle = Addressables.LoadContentCatalogAsync(settings.AddressableAssetsCatalog, false,
                nextSuffix);
            try
            {
                var locator = handle.WaitForCompletion();
                if (handle.Status != AsyncOperationStatus.Succeeded || locator == null)
                {
                    Debug.LogError("ThunderKit cannot reload the game Addressables catalog at " + settings.AddressableAssetsCatalog);
                    return;
                }

                if (gameLocator != null)
                    Addressables.RemoveResourceLocator(gameLocator);
                gameLocator = locator;
                UnsetAllShaders();
                if (providerSuffix != null)
                {
                    var providers = Addressables.ResourceManager.ResourceProviders;
                    for (int i = providers.Count - 1; i >= 0; i--)
                        if (providers[i].ProviderId.EndsWith(providerSuffix, StringComparison.Ordinal))
                            providers.RemoveAt(i);
                }
                providerSuffix = nextSuffix;
                AssignShaders();
            }
            finally
            {
                Addressables.Release(handle);
            }
        }

        private static void ClearSelectionIfUnsavable(object obj)
        {
            if (!Selection.activeObject) return;
            if (Selection.activeObject.hideFlags.HasFlag(HideFlags.DontSave))
                Selection.activeObject = null;
        }

        private static void AssignShaders()
        {
            var settings = GetOrCreateSettings<AddressableGraphicsSettings>();
            SetShader(settings.CustomDeferredShading, BuiltinShaderType.DeferredShading);
            SetShader(settings.CustomDeferredReflection, BuiltinShaderType.DeferredReflections);
            SetShader(settings.CustomDeferredScreenspaceShadows, BuiltinShaderType.ScreenSpaceShadows);
            AddressablesInitialized?.Invoke(null, EventArgs.Empty);
        }

        public static void UnsetAllShaders()
        {
            UnsetShader(null, BuiltinShaderType.DeferredShading);
            UnsetShader(null, BuiltinShaderType.DeferredReflections);
            UnsetShader(null, BuiltinShaderType.ScreenSpaceShadows);
        }
        public static void SetShader(string address, BuiltinShaderType shaderType)
        {
            if (!string.IsNullOrEmpty(address))
            {
                if (shaderLoads.TryGetValue(shaderType, out var load) && load.address == address &&
                    load.handle.IsValid() && load.handle.Status == AsyncOperationStatus.Succeeded && load.handle.Result)
                {
                    GraphicsSettings.SetCustomShader(shaderType, load.handle.Result);
                    GraphicsSettings.SetShaderMode(shaderType, BuiltinShaderMode.UseCustom);
                    return;
                }

                var aop = Addressables.LoadAssetAsync<Shader>(address);
                bool assigned = false;
                try
                {
                    aop.WaitForCompletion();
                    if (aop.Result is Shader shader && shader)
                    {
                        UnsetShader(address, shaderType);
                        shaderLoads[shaderType] = (address, aop);
                        assigned = true;
                        shader.hideFlags = HideFlags.HideAndDontSave;
                        GraphicsSettings.SetCustomShader(shaderType, shader);
                        GraphicsSettings.SetShaderMode(shaderType, BuiltinShaderMode.UseCustom);
                    }
                    else
                        Debug.LogError($"Custom {shaderType} shader at address \"{address}\" can't be assigned");
                }
                finally
                {
                    if (!assigned && aop.IsValid())
                        Addressables.Release(aop);
                }
            }
            else
                UnsetShader(address, shaderType);
        }

        public static void UnsetShader(string address, BuiltinShaderType shaderType)
        {
            GraphicsSettings.SetCustomShader(shaderType, null);
            GraphicsSettings.SetShaderMode(shaderType, BuiltinShaderMode.UseBuiltin);
            if (shaderLoads.TryGetValue(shaderType, out var load))
            {
                shaderLoads.Remove(shaderType);
                if (load.handle.IsValid())
                    Addressables.Release(load.handle);
            }
        }

        public override void CreateSettingsUI(VisualElement rootElement)
        {
            base.CreateSettingsUI(rootElement);
            rootElement.Add(new Button(ReloadAddressables) { text = "Reload" });
        }
    }
}
#endif
