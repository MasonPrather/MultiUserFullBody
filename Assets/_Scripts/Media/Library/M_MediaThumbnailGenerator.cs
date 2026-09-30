/*
 * Script Name: M_MediaThumbnailGenerator.cs
 * Description: Texture resize and JPG encoding helpers for import and gallery thumbnails.
 * Project Role: Keeps image processing on Unity's main thread and releases temporary GPU/CPU textures.
 */

using UnityEngine;

public static class M_MediaThumbnailGenerator
{
    public static Texture2D ResizeReadable(Texture2D source, int maxLongEdge)
    {
        if (source == null)
            return null;

        int clampedEdge = Mathf.Max(1, maxLongEdge);
        int width = source.width;
        int height = source.height;
        int longEdge = Mathf.Max(width, height);

        if (longEdge <= clampedEdge)
        {
            Texture2D copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
            copy.SetPixels32(source.GetPixels32());
            copy.Apply(false, false);
            return copy;
        }

        float scale = clampedEdge / (float)longEdge;
        int targetWidth = Mathf.Max(1, Mathf.RoundToInt(width * scale));
        int targetHeight = Mathf.Max(1, Mathf.RoundToInt(height * scale));

        RenderTexture previous = RenderTexture.active;
        RenderTexture renderTexture = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
        Texture2D resized = null;

        try
        {
            Graphics.Blit(source, renderTexture);
            RenderTexture.active = renderTexture;
            resized = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            resized.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            resized.Apply(false, false);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTexture);
        }

        return resized;
    }

    public static byte[] EncodeJpg(Texture2D source, int maxLongEdge, int quality, out int width, out int height)
    {
        width = 0;
        height = 0;

        Texture2D resized = ResizeReadable(source, maxLongEdge);
        if (resized == null)
            return null;

        try
        {
            width = resized.width;
            height = resized.height;
            return ImageConversion.EncodeToJPG(resized, Mathf.Clamp(quality, 1, 100));
        }
        finally
        {
            Object.Destroy(resized);
        }
    }

    public static byte[] EncodeThumbnailUnderLimit(Texture2D source, int maxLongEdge, int startingQuality, int maxBytes, out int width, out int height)
    {
        width = 0;
        height = 0;

        Texture2D resized = ResizeReadable(source, maxLongEdge);
        if (resized == null)
            return null;

        try
        {
            width = resized.width;
            height = resized.height;

            int quality = Mathf.Clamp(startingQuality, 35, 95);
            byte[] jpg = ImageConversion.EncodeToJPG(resized, quality);
            while (jpg != null && jpg.Length > maxBytes && quality > 45)
            {
                quality -= 10;
                jpg = ImageConversion.EncodeToJPG(resized, quality);
            }

            return jpg;
        }
        finally
        {
            Object.Destroy(resized);
        }
    }

    public static byte[] EncodeVideoPlaceholderJpg(int edge, int quality, out int width, out int height)
    {
        int size = Mathf.Clamp(edge, 64, 1024);
        width = size;
        height = size;

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        try
        {
            Color32 backgroundTop = new Color32(27, 38, 59, 255);
            Color32 backgroundBottom = new Color32(8, 13, 22, 255);
            Color32 panel = new Color32(15, 23, 42, 255);
            Color32 accent = new Color32(248, 250, 252, 255);

            for (int y = 0; y < size; y++)
            {
                float t = y / (float)Mathf.Max(1, size - 1);
                Color32 row = Color32.Lerp(backgroundBottom, backgroundTop, t);
                for (int x = 0; x < size; x++)
                    texture.SetPixel(x, y, row);
            }

            int margin = Mathf.RoundToInt(size * 0.16f);
            for (int y = margin; y < size - margin; y++)
            {
                for (int x = margin; x < size - margin; x++)
                    texture.SetPixel(x, y, panel);
            }

            DrawPlayTriangle(texture, accent);
            texture.Apply(false, false);
            return ImageConversion.EncodeToJPG(texture, Mathf.Clamp(quality, 1, 100));
        }
        finally
        {
            Object.Destroy(texture);
        }
    }

    private static void DrawPlayTriangle(Texture2D texture, Color32 color)
    {
        int size = texture.width;
        int left = Mathf.RoundToInt(size * 0.39f);
        int right = Mathf.RoundToInt(size * 0.68f);
        int top = Mathf.RoundToInt(size * 0.32f);
        int bottom = Mathf.RoundToInt(size * 0.68f);
        int centerY = size / 2;

        for (int x = left; x <= right; x++)
        {
            float normalized = (x - left) / (float)Mathf.Max(1, right - left);
            int halfHeight = Mathf.RoundToInt((bottom - top) * 0.5f * normalized);
            for (int y = centerY - halfHeight; y <= centerY + halfHeight; y++)
            {
                if (x >= 0 && x < size && y >= 0 && y < size)
                    texture.SetPixel(x, y, color);
            }
        }
    }
}
