using BepInEx.Configuration;
using MSU.Config;
using RiskOfOptions;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;
using System;
using System.Collections.Generic;
using System.IO;

namespace FortunesFromTheScrapyard
{
    public sealed class FFTSConfigureFieldAttribute : RiskOfOptionsConfigureFieldAttribute
    {
        private readonly float? minimum;
        private readonly float? maximum;

        public bool restartRequired { get; set; }

        public FFTSConfigureFieldAttribute(string fileIdentifier) : base(fileIdentifier)
        {
        }

        public FFTSConfigureFieldAttribute(string fileIdentifier, float minimum, float maximum) : base(fileIdentifier)
        {
            if (float.IsNaN(minimum) || float.IsInfinity(minimum) ||
                float.IsNaN(maximum) || float.IsInfinity(maximum) || minimum >= maximum)
                throw new ArgumentException("Option bounds must be finite and the minimum must be less than the maximum.");

            this.minimum = minimum;
            this.maximum = maximum;
        }

        protected override void OnConfigured(ConfigFile configFile, object value)
        {
            if (configFileIdentifier == FFTSConfig.ID_SURVIVORS
                && (!string.IsNullOrEmpty(configSectionOverride) || !string.IsNullOrEmpty(configNameOverride)))
            {
                switch (value)
                {
                    case float floatDefault:
                        MigrateLegacyConfigEntry(configFile, floatDefault);
                        break;
                    case int intDefault:
                        MigrateLegacyConfigEntry(configFile, intDefault);
                        break;
                    case bool boolDefault:
                        MigrateLegacyConfigEntry(configFile, boolDefault);
                        break;
                    case string stringDefault:
                        MigrateLegacyConfigEntry(configFile, stringDefault);
                        break;
                    default:
                        throw new NotSupportedException($"Config migration does not support {value.GetType().Name}.");
                }
            }

            BaseOption option;
            if (value is bool)
            {
                option = new CheckBoxOption(GetConfigEntry<bool>(), restartRequired);
            }
            else
            {
                if (!minimum.HasValue || !maximum.HasValue)
                    throw new InvalidOperationException($"Numeric option {attachedMemberInfo.Name} requires slider bounds.");

                switch (value)
                {
                    case float defaultValue:
                        ConfigEntry<float> floatEntry = GetConfigEntry<float>();
                        option = new SliderOption(floatEntry, new SliderConfig
                        {
                            min = Math.Min(minimum.Value, Math.Min(defaultValue, floatEntry.Value)),
                            max = Math.Max(maximum.Value, Math.Max(defaultValue, floatEntry.Value)),
                            FormatString = "{0:0.#####}",
                            restartRequired = restartRequired
                        });
                        break;
                    case int defaultValue:
                        ConfigEntry<int> intEntry = GetConfigEntry<int>();
                        option = new IntSliderOption(intEntry, new IntSliderConfig
                        {
                            min = Math.Min(checked((int)minimum.Value), Math.Min(defaultValue, intEntry.Value)),
                            max = Math.Max(checked((int)maximum.Value), Math.Max(defaultValue, intEntry.Value)),
                            restartRequired = restartRequired
                        });
                        break;
                    default:
                        throw new NotSupportedException($"Option type {value.GetType().Name} is not supported.");
                }
            }

            bool separateEntry = ConfigSystem.ShouldCreateSeparateRiskOfOptionsEntry(configFile);
            string fileName = Path.GetFileNameWithoutExtension(configFile.ConfigFilePath);
            string category = fileName.StartsWith(FFTSConfig.PREFIX, StringComparison.Ordinal)
                ? fileName.Substring(FFTSConfig.PREFIX.Length)
                : fileName;
            string guid = separateEntry ? modGUID + "." + fileName : modGUID;
            string name = separateEntry ? modName + " - " + category : modName;
            ModSettingsManager.AddOption(option, guid, name);
        }

        private void MigrateLegacyConfigEntry<T>(ConfigFile configFile, T defaultValue)
        {
            ConfigEntry<T> currentEntry = GetConfigEntry<T>();
            string legacyKey = MSU.MSUtil.NicifyString(attachedMemberInfo.Name);
            string survivorName = configSectionOverride.Split(new[] { " - " }, 2, StringSplitOptions.None)[0];
            string[] legacySections =
            {
                MSU.MSUtil.NicifyString(survivorName),
                MSU.MSUtil.NicifyString(attachedMemberInfo.DeclaringType.Name)
            };

            bool previousSaveOnConfigSet = configFile.SaveOnConfigSet;
            bool migrated = false;
            configFile.SaveOnConfigSet = false;
            try
            {
                foreach (string section in legacySections)
                {
                    if (section == currentEntry.Definition.Section && legacyKey == currentEntry.Definition.Key)
                        continue;

                    ConfigEntry<T> legacyEntry = configFile.Bind(section, legacyKey, defaultValue, string.Empty);
                    if (EqualityComparer<T>.Default.Equals(currentEntry.Value, defaultValue)
                        && !EqualityComparer<T>.Default.Equals(legacyEntry.Value, defaultValue))
                    {
                        currentEntry.Value = legacyEntry.Value;
                        migrated = true;
                    }
                    configFile.Remove(legacyEntry.Definition);
                }
            }
            finally
            {
                configFile.SaveOnConfigSet = previousSaveOnConfigSet;
            }

            if (migrated)
                configFile.Save();
        }
    }
}
