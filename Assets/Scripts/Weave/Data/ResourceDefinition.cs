using UnityEngine;

namespace Weave.Data
{
    [CreateAssetMenu(menuName = "Weave/Resource Definition")]
    public sealed class ResourceDefinition : ScriptableObject
    {
        [SerializeField] private string resourceId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private float carryWeightPerUnit = 1f;

        public string ResourceId => resourceId;
        public string DisplayName => displayName;
        public float CarryWeightPerUnit => carryWeightPerUnit;
    }
}
