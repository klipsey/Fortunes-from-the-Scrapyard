using MSU;
using MSU.Config;
using RoR2;
using RoR2.ContentManagement;
using RoR2.Items;
using UnityEngine;
using UnityEngine.Networking;
using static R2API.RecalculateStatsAPI;

namespace FortunesFromTheScrapyard.Items
{
    public class LostRecord : FFTSItem
    {
        public const string TOKEN = "FFTS_ITEM_LOSTRECORD_DESCRIPTION";

        [FFTSConfigureField(FFTSConfig.ID_ITEMS, 0f, 100f)]
        [FormatToken(TOKEN, 0)]
        public static float cooldownReduction = 7.5f;

        [FFTSConfigureField(FFTSConfig.ID_ITEMS, 0f, 100f)]
        [FormatToken(TOKEN, 1)]
        public static float cooldownReductionStack = 7.5f;

        [FFTSConfigureField(FFTSConfig.ID_ITEMS)]
        public static bool recordEnabled = true;

        public static GameObject RecordEffect;

        private static NetworkSoundEventDef recordSfx1;
        private static NetworkSoundEventDef recordSfx2;
        private static NetworkSoundEventDef recordSfx3;
        public override void Initialize()
        {
            RecordEffect = assetCollection.FindAsset<GameObject>("RecordEffect");

            recordSfx1 = FFTSContent.CreateAndAddNetworkSoundEventDef("sfx_cd_1");
            recordSfx2 = FFTSContent.CreateAndAddNetworkSoundEventDef("sfx_cd_2");
            recordSfx3 = FFTSContent.CreateAndAddNetworkSoundEventDef("sfx_cd_3");

            On.RoR2.GenericSkill.OnExecute += GenericSkill_OnExecute;
        }

        private void GenericSkill_OnExecute(On.RoR2.GenericSkill.orig_OnExecute orig, GenericSkill skillSlot)
        {
            orig.Invoke(skillSlot);

            if (skillSlot.characterBody.HasItem(FFTSContent.Items.LostRecord))
            {

                GenericSkill primary = skillSlot.characterBody.skillLocator.primary;
                GenericSkill secondary = skillSlot.characterBody.skillLocator.secondary;
                GenericSkill utility = skillSlot.characterBody.skillLocator.utility;
                GenericSkill special = skillSlot.characterBody.skillLocator.special;
                bool meetsCDRequirement = skillSlot.baseRechargeInterval > 1f && skillSlot.rechargeStock > 0 && skillSlot.skillDef.stockToConsume > 0;

                if (meetsCDRequirement)
                {
                    float refund = Util.ConvertAmplificationPercentageIntoReductionPercentage(GetStackValue(cooldownReduction, cooldownReductionStack, skillSlot.characterBody.GetItemCount(FFTSContent.Items.LostRecord))) / skillSlot.rechargeStock;
                    if (skillSlot != primary) primary.rechargeStopwatch += primary.cooldownRemaining * (refund / 100f);
                    if (skillSlot != secondary) secondary.rechargeStopwatch += secondary.cooldownRemaining * (refund / 100f);
                    if (skillSlot != utility) utility.rechargeStopwatch += utility.cooldownRemaining * (refund / 100f);
                    if (skillSlot != special) special.rechargeStopwatch += special.cooldownRemaining * (refund / 100f);

                    if (recordEnabled)
                    {
                        int randomSFX;
                        if (skillSlot == primary)
                        {
                            randomSFX = UnityEngine.Random.Range(0, 1);

                            switch (randomSFX)
                            {
                                case 0:
                                    {
                                        PlayRecordSound(recordSfx1, skillSlot.characterBody);
                                        break;
                                    }
                                case 1:
                                    {
                                        PlayRecordSound(recordSfx2, skillSlot.characterBody);
                                        break;
                                    }
                            }
                        }
                        else
                        {
                            if (skillSlot == secondary) PlayRecordSound(recordSfx1, skillSlot.characterBody);
                            if (skillSlot == utility) PlayRecordSound(recordSfx2, skillSlot.characterBody);
                            if (skillSlot == special) PlayRecordSound(recordSfx3, skillSlot.characterBody);
                        }
                    }

                    EffectManager.SimpleImpactEffect(RecordEffect, skillSlot.characterBody.corePosition, Vector3.up, transmit: true);
                }
            }
        }

        private static void PlayRecordSound(NetworkSoundEventDef sound, CharacterBody body)
        {
            EffectManager.SimpleSoundEffect(sound.index, body.corePosition, true);
        }

        public override void ModifyContentPack(ContentPack contentPack)
        {
            contentPack.AddContentFromAssetCollection(assetCollection);
        }

        public override bool IsAvailable(ContentPack contentPack)
        {
            return true;
        }

        public override FFTSAssetRequest LoadAssetRequest()
        {
            return FFTSAssets.LoadAssetAsync<ItemAssetCollection>("acLostRecord", FFTSBundle.Items);
        }
    }
}
