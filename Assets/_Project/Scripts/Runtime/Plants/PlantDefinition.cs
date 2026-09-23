using UnityEngine;

namespace BTP.Plants
{
    /// <summary>One selectable plant: identity text plus its farm and lab models.</summary>
    [CreateAssetMenu(menuName = "BTP/Plant Definition", fileName = "Plant")]
    public sealed class PlantDefinition : ScriptableObject
    {
        [Tooltip("Stable lowercase ID used by the selection state, e.g. \"tomato\".")]
        [SerializeField] string plantId;
        [SerializeField] string displayName;
        [SerializeField] string scientificName;
        [SerializeField] string family;
        [SerializeField] string plantType;
        [SerializeField, TextArea(2, 5)] string summary;
        [SerializeField] GameObject farmPrefab;
        [SerializeField] GameObject labPrefab;
        [Tooltip("Only Tomato has the illustrative recovery demo.")]
        [SerializeField] bool hasRecoveryDemo;

        public string PlantId => plantId;
        public string DisplayName => displayName;
        public string ScientificName => scientificName;
        public string Family => family;
        public string PlantType => plantType;
        public string Summary => summary;
        public GameObject FarmPrefab => farmPrefab;
        public GameObject LabPrefab => labPrefab;
        public bool HasRecoveryDemo => hasRecoveryDemo;

        internal void Configure(string id, string name, string scientific, string familyName, string type,
            string text, bool recoveryDemo)
        {
            plantId = id;
            displayName = name;
            scientificName = scientific;
            family = familyName;
            plantType = type;
            summary = text;
            hasRecoveryDemo = recoveryDemo;
        }

        internal void SetPrefabs(GameObject farm, GameObject lab)
        {
            farmPrefab = farm;
            labPrefab = lab;
        }
    }
}
