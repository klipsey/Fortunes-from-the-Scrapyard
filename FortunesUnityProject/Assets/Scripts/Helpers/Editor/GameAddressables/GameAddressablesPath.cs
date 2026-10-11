using System;
using System.IO;
using ThunderKit.Core.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets.Initialization;

namespace Fortunes.Editor
{
    [InitializeOnLoad]
    public sealed class GameAddressablesPath : AssetPostprocessor
    {
        private const string SettingsPath = "Assets/ThunderKitSettings/ThunderKitSettings.asset";

        static GameAddressablesPath()
        {
            // ThunderKit resolves catalog paths in InitializeOnLoadMethod, before asset postprocessing.
            ConfigurePath();
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredEditMode)
                ConfigurePath();
        }

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload)
        {
            if (!didDomainReload && Array.IndexOf(importedAssets, SettingsPath) < 0 &&
                Array.IndexOf(movedAssets, SettingsPath) < 0)
                return;

            ConfigurePath();
        }

        private static void ConfigurePath()
        {
            // Before asset import, GetOrCreateSettings can overwrite settings that are not yet loadable.
            var settings = AssetDatabase.LoadAssetAtPath<ThunderKitSettings>(SettingsPath);
            string path = settings ? settings.AddressableAssetsPath : null;
            if (!settings && File.Exists(SettingsPath))
            {
                var objects = UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget(SettingsPath);
                foreach (var obj in objects)
                {
                    if (obj is ThunderKitSettings loadedSettings)
                        path = loadedSettings.AddressableAssetsPath;
                    UnityEngine.Object.DestroyImmediate(obj);
                }
            }

            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("Fortunes cannot load ThunderKit Settings at " + SettingsPath);
                return;
            }

            if (!File.Exists(Path.Combine(path, "catalog.json")))
            {
                Debug.LogError("Fortunes cannot locate the game Addressables catalog. Check the game path in ThunderKit Settings: " + path);
                return;
            }

            // Bundle loading and editor save hooks can run before TK_ADDRESSABLE is restored.
            AddressablesRuntimeProperties.SetPropertyValue(
                "UnityEngine.AddressableAssets.Addressables.RuntimePath", path.Replace('\\', '/'));
        }
    }
}
