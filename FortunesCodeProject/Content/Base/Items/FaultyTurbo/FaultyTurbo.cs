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
    public class FaultyTurbo : FFTSItem
    {
        public const string TOKEN = "FFTS_ITEM_FAULTYTURBO_DESCRIPTION";

        [FFTSConfigureField(FFTSConfig.ID_ITEMS, 0f, 30f)]
        [FormatToken(TOKEN, 0)]
        public static int maxStacks = 3;
        [FFTSConfigureField(FFTSConfig.ID_ITEMS, 0f, 30f)]
        [FormatToken(TOKEN, 1)]
        public static int maxStacksPerStack = 1;

        [FFTSConfigureField(FFTSConfig.ID_ITEMS, 0f, 2f)]
        [FormatToken(TOKEN, FormatTokenAttribute.OperationTypeEnum.MultiplyByN, 100, 2)]
        public static float movespeedBonusPerInterval = 0.1f; //0.1f

        [FFTSConfigureField(FFTSConfig.ID_ITEMS, 0f, 2f)]
        [FormatToken(TOKEN, FormatTokenAttribute.OperationTypeEnum.MultiplyByN, 100, 3)]
        public static float movespeedBonusPerIntervalStack = 0.025f; //0.1f

        [FFTSConfigureField(FFTSConfig.ID_ITEMS, 0.05f, 10f)]
        [FormatToken(TOKEN, 4)]
        public static float checkInterval = 1f;

        [FFTSConfigureField(FFTSConfig.ID_ITEMS, 0f, 30f)]
        public static float decayDelay = 3f;

        [FFTSConfigureField(FFTSConfig.ID_ITEMS, 0.05f, 10f)]
        public static float decayInterval = 0.5f;

        internal static NetworkSoundEventDef turboSfx;
        public override void Initialize()
        {
            turboSfx = FFTSContent.CreateAndAddNetworkSoundEventDef("sfx_turbo_start");
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
            return FFTSAssets.LoadAssetAsync<ItemAssetCollection>("acFaultyTurbo", FFTSBundle.Items);
        }

        public class FaultyTurboBehaviour : BaseItemBodyBehavior, IBodyStatArgModifier
        {
            [ItemDefAssociation]
            public static ItemDef GetItemDef() => FFTSContent.Items.FaultyTurbo;

            private float gainTimer = 0f;
            private float decayTimer = 0f;
            private int previousBuffCount = 0;
            public void ModifyStatArguments(StatHookEventArgs args)
            {
                if (body.HasBuff(FFTSContent.Buffs.bdFaultyTurbo))
                {
                    args.moveSpeedMultAdd += GetStackValue(movespeedBonusPerInterval, movespeedBonusPerIntervalStack, stack) * body.GetBuffCount(FFTSContent.Buffs.bdFaultyTurbo);
                }
            }

            private void FixedUpdate()
            {
                float maxBuffCount = GetStackValue(maxStacks, maxStacksPerStack, stack);
                int buffCount = body.GetBuffCount(FFTSContent.Buffs.bdFaultyTurbo);

                if (buffCount >= maxBuffCount && previousBuffCount < maxBuffCount && body.master?.playerCharacterMasterController?.networkUser?.isLocalPlayer == true)
                {
                    EffectManager.SimpleSoundEffect(turboSfx.index, body.corePosition, false);
                }
                previousBuffCount = buffCount;

                if (NetworkServer.active)
                {
                    UpdateStacks(buffCount, maxBuffCount);
                }
            }

            private void UpdateStacks(int buffCount, float maxBuffCount)
            {
                if (body.isSprinting)
                {
                    gainTimer += Time.fixedDeltaTime;
                    if (gainTimer >= checkInterval)
                    {
                        gainTimer = 0f;
                        decayTimer = 0f;

                        if (buffCount < maxBuffCount)
                        {
                            body.AddBuff(FFTSContent.Buffs.bdFaultyTurbo);
                        }
                    }
                    return;
                }

                gainTimer = 0f;
                if (buffCount <= 0)
                {
                    decayTimer = 0f;
                    return;
                }

                decayTimer += Time.fixedDeltaTime;
                if (decayTimer >= decayDelay)
                {
                    body.RemoveBuff(FFTSContent.Buffs.bdFaultyTurbo);
                    decayTimer = decayDelay - decayInterval;
                }
            }
            private void OnDisable()
            {
                if (NetworkServer.active)
                {
                    if (body.HasBuff(FFTSContent.Buffs.bdFaultyTurbo))
                    {
                        body.SetBuffCount(FFTSContent.Buffs.bdFaultyTurbo.buffIndex, 0);
                    }
                }
            }
        }
    }
}
