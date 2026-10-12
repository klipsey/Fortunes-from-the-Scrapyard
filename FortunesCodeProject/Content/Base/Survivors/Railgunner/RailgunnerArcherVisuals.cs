using RoR2;
using UnityEngine;

namespace FortunesFromTheScrapyard
{
    internal sealed class RailgunnerArcherVisuals : MonoBehaviour
    {
        [SerializeField] private GameObject[] visualObjects;
        [SerializeField] private bool[] defaultActiveStates;
        private ModelSkinController controller;

        internal void Configure(GameObject[] objects)
        {
            visualObjects = objects;
            defaultActiveStates = new bool[objects.Length];
            for (int i = 0; i < objects.Length; i++)
                defaultActiveStates[i] = objects[i].activeSelf;
        }

        private void Awake()
        {
            controller = GetComponent<ModelSkinController>();
        }

        private void OnEnable()
        {
            controller.onSkinApplied += OnSkinApplied;
        }

        private void OnDisable()
        {
            controller.onSkinApplied -= OnSkinApplied;
        }

        private void OnSkinApplied(int skinIndex)
        {
            bool isArcher = controller.skins[skinIndex].name == "skinRailgunnerArcher";
            for (int i = 0; i < visualObjects.Length; i++)
                visualObjects[i].SetActive(!isArcher && defaultActiveStates[i]);
        }
    }
}
