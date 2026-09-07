using UnityEngine;

namespace Weave.World
{
    [CreateAssetMenu(menuName = "Weave/Village Grid Settings")]
    public sealed class VillageGridSettings : ScriptableObject
    {
        [SerializeField] private Vector2 origin = Vector2.zero;
        [SerializeField, Min(0.1f)] private float cellSize = 1f;

        public Vector2 Origin => origin;
        public float CellSize => Mathf.Max(0.1f, cellSize);
    }
}
