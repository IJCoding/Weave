using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
#endif

namespace Weave.World.WFC
{
    public sealed class WFCPathLibrary : MonoBehaviour
    {
        [SerializeField] private string spriteFolderPath = "Assets/Spritesheet/Paths";
        [SerializeField] private bool autoBuildFromSpriteList;
        [SerializeField] private bool loadSpritesFromFolderInEditor = true;
        [SerializeField] private List<Sprite> sourceSprites = new List<Sprite>();
        [SerializeField] private List<WFCPathTileDefinition> tileDefinitions = new List<WFCPathTileDefinition>();

        public IReadOnlyList<WFCPathTileDefinition> TileDefinitions => tileDefinitions;

        public List<WFCPathTileDefinition> GetUsableTileDefinitions()
        {
            var result = new List<WFCPathTileDefinition>();
            foreach (var definition in tileDefinitions)
            {
                if (definition == null)
                {
                    continue;
                }

                if (definition.Sprite == null && definition.TileOverride == null)
                {
                    continue;
                }

                definition.ApplySpecialRules();
                result.Add(definition);
            }

            return result;
        }

        [ContextMenu("Refresh Source Sprites (Editor)")]
        public void RefreshSourceSpritesFromFolder()
        {
#if UNITY_EDITOR
            if (!loadSpritesFromFolderInEditor)
            {
                return;
            }

            var spriteGuids = AssetDatabase.FindAssets("t:Sprite", new[] { spriteFolderPath });
            var loadedSprites = new List<Sprite>();
            foreach (var guid in spriteGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null)
                {
                    loadedSprites.Add(sprite);
                }
            }

            sourceSprites = loadedSprites.OrderBy(sprite => sprite.name, StringComparer.OrdinalIgnoreCase).ToList();
            EditorUtility.SetDirty(this);
#else
            Debug.LogWarning("RefreshSourceSpritesFromFolder is editor-only.");
#endif
        }

        [ContextMenu("Build Tile Definitions")]
        public void BuildTileDefinitions()
        {
#if UNITY_EDITOR
            if (loadSpritesFromFolderInEditor)
            {
                RefreshSourceSpritesFromFolder();
            }
#endif

            var existingByKey = new Dictionary<string, WFCPathTileDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var existing in tileDefinitions)
            {
                if (existing == null || existing.Sprite == null)
                {
                    continue;
                }

                var key = MakeDefinitionKey(existing.Sprite.name, existing.PathEndEntranceDirection);
                if (!existingByKey.ContainsKey(key))
                {
                    existingByKey.Add(key, existing);
                }
            }

            var rebuilt = new List<WFCPathTileDefinition>();
            foreach (var sprite in sourceSprites)
            {
                if (sprite == null)
                {
                    continue;
                }

                var parsed = ParseSpriteName(sprite.name);
                if (parsed.isPathEnd && parsed.pathEndDirection == PathSocketDirection.None)
                {
                    AddOrCreateDefinition(sprite, parsed, PathSocketDirection.Top, existingByKey, rebuilt);
                    AddOrCreateDefinition(sprite, parsed, PathSocketDirection.Bottom, existingByKey, rebuilt);
                    AddOrCreateDefinition(sprite, parsed, PathSocketDirection.Left, existingByKey, rebuilt);
                    AddOrCreateDefinition(sprite, parsed, PathSocketDirection.Right, existingByKey, rebuilt);
                }
                else
                {
                    var direction = parsed.isPathEnd ? parsed.pathEndDirection : PathSocketDirection.None;
                    AddOrCreateDefinition(sprite, parsed, direction, existingByKey, rebuilt);
                }
            }

            tileDefinitions = rebuilt;
        }

        private void OnValidate()
        {
            if (!autoBuildFromSpriteList)
            {
                return;
            }

            BuildTileDefinitions();
        }

        public static (bool hasTop, bool hasBottom, bool hasLeft, bool hasRight, bool isBlock, bool isCrossing, bool isPathEnd, PathSocketDirection pathEndDirection)
            ParseSpriteName(string rawName)
        {
            var lowered = string.IsNullOrWhiteSpace(rawName)
                ? string.Empty
                : rawName.Trim().ToLowerInvariant();

            var hasTop = ContainsDirectionToken(lowered, "top");
            var hasBottom = ContainsDirectionToken(lowered, "bottom");
            var hasLeft = ContainsDirectionToken(lowered, "left");
            var hasRight = ContainsDirectionToken(lowered, "right");

            if (lowered.Contains("vertical"))
            {
                hasTop = true;
                hasBottom = true;
            }

            if (lowered.Contains("horizontal"))
            {
                hasLeft = true;
                hasRight = true;
            }

            var isCrossing = lowered.Contains("crossing") || lowered.Contains("cross");
            if (isCrossing)
            {
                hasTop = true;
                hasBottom = true;
                hasLeft = true;
                hasRight = true;
            }

            var isBlock = lowered.Contains("block");
            var isPathEnd = lowered.Contains("end");

            var pathEndDirection = PathSocketDirection.None;
            if (isPathEnd)
            {
                var directionCount = 0;
                if (ContainsDirectionToken(lowered, "top"))
                {
                    directionCount++;
                    pathEndDirection = PathSocketDirection.Top;
                }

                if (ContainsDirectionToken(lowered, "bottom"))
                {
                    directionCount++;
                    pathEndDirection = PathSocketDirection.Bottom;
                }

                if (ContainsDirectionToken(lowered, "left"))
                {
                    directionCount++;
                    pathEndDirection = PathSocketDirection.Left;
                }

                if (ContainsDirectionToken(lowered, "right"))
                {
                    directionCount++;
                    pathEndDirection = PathSocketDirection.Right;
                }

                if (directionCount != 1)
                {
                    pathEndDirection = PathSocketDirection.None;
                }

                if (pathEndDirection != PathSocketDirection.None)
                {
                    hasTop = pathEndDirection == PathSocketDirection.Top;
                    hasBottom = pathEndDirection == PathSocketDirection.Bottom;
                    hasLeft = pathEndDirection == PathSocketDirection.Left;
                    hasRight = pathEndDirection == PathSocketDirection.Right;
                }
            }

            if (isBlock)
            {
                hasTop = false;
                hasBottom = false;
                hasLeft = false;
                hasRight = false;
                pathEndDirection = PathSocketDirection.None;
            }

            return (hasTop, hasBottom, hasLeft, hasRight, isBlock, isCrossing, isPathEnd, pathEndDirection);
        }

        private static void AddOrCreateDefinition(
            Sprite sprite,
            (bool hasTop, bool hasBottom, bool hasLeft, bool hasRight, bool isBlock, bool isCrossing, bool isPathEnd, PathSocketDirection pathEndDirection) parsed,
            PathSocketDirection pathEndDirection,
            Dictionary<string, WFCPathTileDefinition> existingByKey,
            List<WFCPathTileDefinition> target)
        {
            var key = MakeDefinitionKey(sprite.name, pathEndDirection);
            if (!existingByKey.TryGetValue(key, out var definition))
            {
                definition = new WFCPathTileDefinition();
                definition.SetWeight(1f);
            }

            definition.SetIdentity(sprite);
            definition.SetTileName(pathEndDirection == PathSocketDirection.None
                ? sprite.name
                : $"{sprite.name}_{pathEndDirection}");
            definition.SetSockets(parsed.hasTop, parsed.hasBottom, parsed.hasLeft, parsed.hasRight);
            definition.SetSpecialFlags(parsed.isBlock, parsed.isCrossing, parsed.isPathEnd);
            definition.SetPathEndDirection(pathEndDirection == PathSocketDirection.None ? parsed.pathEndDirection : pathEndDirection);
            definition.ApplySpecialRules();
            target.Add(definition);
        }

        private static string MakeDefinitionKey(string spriteName, PathSocketDirection direction)
        {
            return string.Concat(spriteName ?? string.Empty, "::", direction.ToString());
        }

        private static bool ContainsDirectionToken(string loweredName, string token)
        {
            if (string.IsNullOrEmpty(loweredName))
            {
                return false;
            }

            var separators = new[] { '_', '-', ' ' };
            var parts = loweredName.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (part == token)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
