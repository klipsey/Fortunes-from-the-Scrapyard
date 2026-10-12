using MSU;
using MSU.Config;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;

namespace FortunesFromTheScrapyard.Survivors.Cloaker
{
    public class CloakerController : MonoBehaviour
    {
        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 0f, 30f, configDescOverride = "Delay after stealth breaks before passive stealth returns, even in combat. Reduced by cooldown reduction.", configSectionOverride = "Cloaker - Cloak", configNameOverride = "Passive Stealth Recovery Delay")]
        [FormatToken(CloakerSurvivor.PASSIVETOKEN, 0)]
        public static float BaseRestealthCooldown = 4f;

        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 0f, 30f, configDescOverride = "Seconds Restealth protects Cloak from enemy proximity reveals. Attacking and taking damage still break stealth.", configSectionOverride = "Cloaker - Restealth", configNameOverride = "Stealth Protection Duration")]
        [FormatToken(CloakerSurvivor.UTILITYTOKEN, 1)]
        public static float BaseGracePeriod = 2f;

        private const float DefaultDetectionRadius = 6f;
        [FFTSConfigureField(FFTSConfig.ID_SURVIVORS, 0f, 50f, configDescOverride = "Enemy proximity reveal radius in meters.", configSectionOverride = "Cloaker - Cloak", configNameOverride = "Enemy Reveal Radius")]
        [FormatToken(CloakerSurvivor.PASSIVETOKEN, 2)]
        public static float DetectionRadius = DefaultDetectionRadius;

        public SkillDef passiveCloakSkillDef;
        public SkillDef passiveAkimboSkillDef;
        public SkillDef akimboPrimarySkillDef;
        public GenericSkill passiveSkillSlot;
        public GameObject mainhandModel;
        public GameObject offhandModel;
        public Animator modelAnimator;

        public bool isAkimbo => passiveSkillSlot && passiveSkillSlot.skillDef == passiveAkimboSkillDef;
        public bool isCloak => passiveSkillSlot && passiveSkillSlot.skillDef == passiveCloakSkillDef;
        public float graceTimer { get; private set; }
        public bool passiveCloakOn { get; private set; }

        private CharacterBody body;
        private SkillLocator skills;
        private bool initialized;
        private bool appliedAkimbo;
        private bool emoting;
        private float restealthTimer;
        private GameObject indicator;
        private Transform radius;
        private Collider[] proximityHits = new Collider[128];

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
            skills = GetComponent<SkillLocator>();
        }

        private void Start() => RefreshPassive();

        private void RefreshPassive()
        {
            if (initialized && appliedAkimbo == isAkimbo) return;
            initialized = true;
            appliedAkimbo = isAkimbo;
            UpdateWeaponVisibility();
            modelAnimator.SetBool("isAkimbo", appliedAkimbo);
            if (appliedAkimbo)
                skills.primary.SetSkillOverride(this, akimboPrimarySkillDef, GenericSkill.SkillOverridePriority.Upgrade);
            else
                skills.primary.UnsetSkillOverride(this, akimboPrimarySkillDef, GenericSkill.SkillOverridePriority.Upgrade);
            body.MarkAllStatsDirty();
            if (appliedAkimbo) BreakStealth();
        }

        public string GetAnimationStateName(string layerName, string stateName)
            => $"{layerName}.{(isAkimbo ? "Akimbo" : "Default")}.{stateName}";

        public void SetEmoting(bool value)
        {
            emoting = value;
            UpdateWeaponVisibility();
        }

        private void UpdateWeaponVisibility()
        {
            if (mainhandModel) mainhandModel.SetActive(!emoting);
            if (offhandModel) offhandModel.SetActive(!emoting && isAkimbo);
        }

        public void StartGracePeriod()
        {
            graceTimer = BaseGracePeriod;
            EnterPassiveStealth();
        }

        public float GetStealthAttackDamage(float damage)
            => body.hasCloakBuff ? damage * (1f + CloakerSurvivor.CloakDamageBonus) : damage;

        private void EnterPassiveStealth()
        {
            restealthTimer = 0f;
            passiveCloakOn = true;
            if (!NetworkServer.active) return;
            bool wasCloaked = body.hasCloakBuff;
            body.ClearTimedBuffs(RoR2Content.Buffs.Cloak);
            body.ClearTimedBuffs(RoR2Content.Buffs.CloakSpeed);
            if (!body.HasBuff(RoR2Content.Buffs.Cloak))
                body.AddBuff(RoR2Content.Buffs.Cloak);
            if (!body.HasBuff(RoR2Content.Buffs.CloakSpeed))
                body.AddBuff(RoR2Content.Buffs.CloakSpeed);
            if (!wasCloaked)
                EffectManager.SimpleSoundEffect(CloakerAssets.StealthSound.index, body.corePosition, true);
        }

        public void BreakStealth()
        {
            if (NetworkServer.active)
            {
                body.ClearTimedBuffs(RoR2Content.Buffs.Cloak);
                body.ClearTimedBuffs(RoR2Content.Buffs.CloakSpeed);
                if (body.HasBuff(RoR2Content.Buffs.Cloak)) body.RemoveBuff(RoR2Content.Buffs.Cloak);
                if (body.HasBuff(RoR2Content.Buffs.CloakSpeed)) body.RemoveBuff(RoR2Content.Buffs.CloakSpeed);
            }
            passiveCloakOn = false;
            restealthTimer = 0f;
            graceTimer = 0f;
        }

        private void FixedUpdate()
        {
            RefreshPassive();
            graceTimer = Mathf.Max(0f, graceTimer - Time.fixedDeltaTime);
            if (NetworkServer.active && body.healthComponent && body.healthComponent.alive && !isAkimbo)
            {
                restealthTimer += Time.fixedDeltaTime;
                float cooldown = Mathf.Min(BaseRestealthCooldown,
                    Mathf.Max(0.5f, BaseRestealthCooldown * skills.primary.cooldownScale - skills.primary.flatCooldownReduction));
                if (restealthTimer >= cooldown && !body.hasCloakBuff && !passiveCloakOn)
                    EnterPassiveStealth();
                if (passiveCloakOn && body.hasCloakBuff && graceTimer <= 0f) CheckProximity();
                if (!body.hasCloakBuff) passiveCloakOn = false;
            }

            bool show = NetworkClient.active && !isAkimbo && body.healthComponent && body.healthComponent.alive && body.hasCloakBuff;
            if (show && !indicator)
            {
                indicator = Instantiate(CloakerAssets.RangeIndicator, body.corePosition, Quaternion.identity);
                radius = indicator.transform.Find("Radius");
            }
            if (indicator)
            {
                indicator.SetActive(show);
                indicator.transform.position = body.corePosition;
                indicator.transform.localScale = Vector3.one * (DetectionRadius / DefaultDetectionRadius);
                radius.gameObject.SetActive(show && graceTimer <= 0f);
            }
        }

        private void CheckProximity()
        {
            int count = Physics.OverlapSphereNonAlloc(body.corePosition, DetectionRadius, proximityHits,
                LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide);
            while (count == proximityHits.Length)
            {
                System.Array.Resize(ref proximityHits, proximityHits.Length * 2);
                count = Physics.OverlapSphereNonAlloc(body.corePosition, DetectionRadius, proximityHits,
                    LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide);
            }
            for (int i = 0; i < count; ++i)
            {
                Collider collider = proximityHits[i];
                HurtBox hurtBox = collider.GetComponent<HurtBox>();
                CharacterBody other = hurtBox && hurtBox.healthComponent
                    ? hurtBox.healthComponent.body : collider.GetComponentInParent<CharacterBody>();
                if (!other || other == body || !other.healthComponent || !other.healthComponent.alive || !other.teamComponent) continue;
                if (other.teamComponent.teamIndex == body.teamComponent.teamIndex) continue;
                BreakStealth();
                break;
            }
            System.Array.Clear(proximityHits, 0, count);
        }

        private void OnDisable()
        {
            SetEmoting(false);
            if (indicator) Destroy(indicator);
            indicator = null;
        }
    }
}
