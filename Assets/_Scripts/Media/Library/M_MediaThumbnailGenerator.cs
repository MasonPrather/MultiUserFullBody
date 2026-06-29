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
}
