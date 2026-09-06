using UnityEngine;

namespace Weave.World
{
    public static class PrototypeSpriteLibrary
    {
        private static Sprite squareSprite;
        private static Sprite circleSprite;

        public static Sprite GetSquareSprite()
        {
            if (squareSprite == null)
            {
                squareSprite = CreateSquareSprite();
            }

            return squareSprite;
        }

        public static Sprite GetCircleSprite()
        {
            if (circleSprite == null)
            {
                circleSprite = CreateCircleSprite(64);
            }

            return circleSprite;
        }

        private static Sprite CreateSquareSprite()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "PrototypeSquareTexture",
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            sprite.name = "PrototypeSquareSprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Sprite CreateCircleSprite(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PrototypeCircleTexture",
                hideFlags = HideFlags.HideAndDontSave
            };
            var center = (size - 1) * 0.5f;
            var radius = size * 0.5f;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var alpha = dx * dx + dy * dy <= radius * radius ? 1f : 0f;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = "PrototypeCircleSprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
