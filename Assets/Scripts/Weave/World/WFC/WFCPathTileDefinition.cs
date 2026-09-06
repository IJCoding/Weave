using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Weave.World.WFC
{
    public enum PathSocketDirection
    {
        None = 0,
        Top = 1,
        Bottom = 2,
        Left = 3,
        Right = 4
    }

    [Serializable]
    public sealed class WFCPathTileDefinition
    {
        [SerializeField] private Sprite sprite;
        [SerializeField] private TileBase tileOverride;
        [SerializeField] private string tileName = string.Empty;
        [SerializeField] private bool hasTop;
        [SerializeField] private bool hasBottom;
        [SerializeField] private bool hasLeft;
        [SerializeField] private bool hasRight;
        [SerializeField] private bool isBlock;
        [SerializeField] private bool isCrossing;
        [SerializeField] private bool isPathEnd;
        [SerializeField] private PathSocketDirection pathEndEntranceDirection = PathSocketDirection.None;
        [SerializeField, Min(0.0001f)] private float weight = 1f;

        [NonSerialized] private TileBase runtimeTileCache;

        public Sprite Sprite => sprite;
        public TileBase TileOverride => tileOverride;
        public string TileName => tileName;
        public bool HasTop => hasTop;
        public bool HasBottom => hasBottom;
        public bool HasLeft => hasLeft;
        public bool HasRight => hasRight;
        public bool IsBlock => isBlock;
        public bool IsCrossing => isCrossing;
        public bool IsPathEnd => isPathEnd;
        public PathSocketDirection PathEndEntranceDirection => pathEndEntranceDirection;
        public float Weight => Mathf.Max(0.0001f, weight);

        public bool IsWalkable
        {
            get
            {
                if (isBlock)
                {
                    return false;
                }

                return hasTop || hasBottom || hasLeft || hasRight || isCrossing || isPathEnd;
            }
        }

        public void SetIdentity(Sprite value)
        {
            sprite = value;
            tileName = value != null ? value.name : string.Empty;
            runtimeTileCache = null;
        }

        public void SetTileName(string value)
        {
            tileName = value ?? string.Empty;
        }

        public void SetSockets(bool top, bool bottom, bool left, bool right)
        {
            hasTop = top;
            hasBottom = bottom;
            hasLeft = left;
            hasRight = right;
        }

        public void SetSpecialFlags(bool block, bool crossing, bool pathEnd)
        {
            isBlock = block;
            isCrossing = crossing;
            isPathEnd = pathEnd;
        }

        public void SetPathEndDirection(PathSocketDirection direction)
        {
            pathEndEntranceDirection = direction;
        }

        public void SetWeight(float value)
        {
            weight = Mathf.Max(0.0001f, value);
        }

        public void ApplySpecialRules()
        {
            if (isCrossing)
            {
                hasTop = true;
                hasBottom = true;
                hasLeft = true;
                hasRight = true;
            }

            if (isPathEnd)
            {
                if (pathEndEntranceDirection == PathSocketDirection.None)
                {
                    pathEndEntranceDirection = InferSingleSocketDirection();
                }

                if (pathEndEntranceDirection != PathSocketDirection.None)
                {
                    hasTop = pathEndEntranceDirection == PathSocketDirection.Top;
                    hasBottom = pathEndEntranceDirection == PathSocketDirection.Bottom;
                    hasLeft = pathEndEntranceDirection == PathSocketDirection.Left;
                    hasRight = pathEndEntranceDirection == PathSocketDirection.Right;
                }
            }

            if (isBlock)
            {
                hasTop = false;
                hasBottom = false;
                hasLeft = false;
                hasRight = false;
                pathEndEntranceDirection = PathSocketDirection.None;
            }
        }

        public bool HasConnection(PathSocketDirection direction)
        {
            switch (direction)
            {
                case PathSocketDirection.Top:
                    return hasTop;
                case PathSocketDirection.Bottom:
                    return hasBottom;
                case PathSocketDirection.Left:
                    return hasLeft;
                case PathSocketDirection.Right:
                    return hasRight;
                default:
                    return false;
            }
        }

        public PathSocketDirection InferSingleSocketDirection()
        {
            PathSocketDirection result = PathSocketDirection.None;
            var count = 0;

            if (hasTop)
            {
                result = PathSocketDirection.Top;
                count++;
            }

            if (hasBottom)
            {
                result = PathSocketDirection.Bottom;
                count++;
            }

            if (hasLeft)
            {
                result = PathSocketDirection.Left;
                count++;
            }

            if (hasRight)
            {
                result = PathSocketDirection.Right;
                count++;
            }

            return count == 1 ? result : PathSocketDirection.None;
        }

        public TileBase GetTileBase()
        {
            if (tileOverride != null)
            {
                return tileOverride;
            }

            if (sprite == null)
            {
                return null;
            }

            if (runtimeTileCache == null)
            {
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.name = string.IsNullOrWhiteSpace(tileName) ? sprite.name : tileName;
                tile.sprite = sprite;
                runtimeTileCache = tile;
            }

            return runtimeTileCache;
        }
    }

    public static class PathSocketDirectionUtility
    {
        public static PathSocketDirection Opposite(PathSocketDirection direction)
        {
            switch (direction)
            {
                case PathSocketDirection.Top:
                    return PathSocketDirection.Bottom;
                case PathSocketDirection.Bottom:
                    return PathSocketDirection.Top;
                case PathSocketDirection.Left:
                    return PathSocketDirection.Right;
                case PathSocketDirection.Right:
                    return PathSocketDirection.Left;
                default:
                    return PathSocketDirection.None;
            }
        }

        public static Vector2Int ToOffset(PathSocketDirection direction)
        {
            switch (direction)
            {
                case PathSocketDirection.Top:
                    return Vector2Int.up;
                case PathSocketDirection.Bottom:
                    return Vector2Int.down;
                case PathSocketDirection.Left:
                    return Vector2Int.left;
                case PathSocketDirection.Right:
                    return Vector2Int.right;
                default:
                    return Vector2Int.zero;
            }
        }

        public static PathSocketDirection FromDelta(Vector3Int delta)
        {
            if (delta.x == 1 && delta.y == 0)
            {
                return PathSocketDirection.Right;
            }

            if (delta.x == -1 && delta.y == 0)
            {
                return PathSocketDirection.Left;
            }

            if (delta.x == 0 && delta.y == 1)
            {
                return PathSocketDirection.Top;
            }

            if (delta.x == 0 && delta.y == -1)
            {
                return PathSocketDirection.Bottom;
            }

            return PathSocketDirection.None;
        }
    }
}
