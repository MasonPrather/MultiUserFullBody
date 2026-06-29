/*
 * Script Name: M_MediaImportController.cs
 * Description: Validates, optimizes, deduplicates, and records phone/device media imports.
 * Project Role: Converts untrusted uploads into privacy-preserving, permanent Quest-local gallery files.
 */

using System;
using System.IO;
using UnityEngine;

public class M_MediaImportController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private M_MediaLibrary mediaLibrary;

    [Header("Image Import Limits")]
    [Tooltip("JPG and PNG inputs larger than this are rejected before Unity decodes them.")]
    [SerializeField] private int maxInputBytes = 20 * 1024 * 1024;

    [Tooltip("Optimized stored/share JPG long edge. 1600-2048 is a good first-release range for Quest and NGO fallback transfer.")]
    [SerializeField] private int maxStoredLongEdge = 1600;

    [SerializeField, Range(1, 100)] private int storedJpgQuality = 82;

    [Tooltip("Hard reject optimized JPGs above this size for the first implementation.")]
    [SerializeField] private int maxOptimizedBytes = 5 * 1024 * 1024;

    [SerializeField] private int thumbnailLongEdge = 512;
    [SerializeField, Range(1, 100)] private int thumbnailJpgQuality = 78;
    [SerializeField] private int thumbnailMaxBytes = 64 * 1024;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    public M_MediaLibrary Library
    {
        get
        {
            ResolveReferences();
            return mediaLibrary;
        }
    }

    private void Awake()
    {
        ResolveReferences();
    }

    public M_MediaImportResult ImportImageBytes(byte[] inputBytes, string mime, string originalFileName)
    {
        ResolveReferences();

        if (mediaLibrary == null)
            return M_MediaImportResult.Fail(M_MediaImportFailure.IoError, "Media library is not configured.");

        mediaLibrary.Initialize();

        if (inputBytes == null || inputBytes.Length == 0)
            return M_MediaImportResult.Fail(M_MediaImportFailure.EmptyInput, "No file was received.");

        if (inputBytes.Length > maxInputBytes)
            return M_MediaImportResult.Fail(M_MediaImportFailure.TooLarge, $"That file is too large. Please choose a JPG or PNG under {FormatBytes(maxInputBytes)}.");

        string normalizedMime = NormalizeMime(mime, originalFileName, inputBytes);
        if (normalizedMime != "image/jpeg" && normalizedMime != "image/png")
            return M_MediaImportResult.Fail(M_MediaImportFailure.UnsupportedType, "Only JPG and PNG photos are supported right now. Video sharing is intentionally disabled for this release.");

        Texture2D decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(decoded, inputBytes, markNonReadable: false))
                return M_MediaImportResult.Fail(M_MediaImportFailure.InvalidImage, "The uploaded file could not be decoded as a JPG or PNG photo.");

            byte[] optimized = M_MediaThumbnailGenerator.EncodeJpg(decoded, maxStoredLongEdge, storedJpgQuality, out int storedWidth, out int storedHeight);
            if (optimized == null || optimized.Length == 0)
                return M_MediaImportResult.Fail(M_MediaImportFailure.InvalidImage, "The photo could not be optimized for Quest storage.");

            if (optimized.Length > maxOptimizedBytes)
            {
                return M_MediaImportResult.Fail(
                    M_MediaImportFailure.TooLarge,
                    $"This photo is still too large after optimization ({FormatBytes(optimized.Length)}). Please choose a smaller image.");
            }

            string sha256 = M_MediaHashUtility.Sha256Hex(optimized);
            M_MediaRecord existing = mediaLibrary.GetBySha256(sha256);
            if (existing != null && mediaLibrary.ContainsCompleteFiles(existing))
            {
                if (verboseLogging)
                    Debug.Log($"[M_MediaImportController] Duplicate import reused mediaId={existing.mediaId}, sha={sha256}.");

                return M_MediaImportResult.Ok(
                    existing,
                    mediaLibrary.ResolveFullPath(existing),
                    mediaLibrary.ResolveThumbnailPath(existing),
                    duplicate: true,
                    message: "This photo is already in your Quest gallery.");
            }

            byte[] thumb = M_MediaThumbnailGenerator.EncodeThumbnailUnderLimit(
                decoded,
                thumbnailLongEdge,
                thumbnailJpgQuality,
                thumbnailMaxBytes,
                out _,
                out _);

            if (thumb == null || thumb.Length == 0)
                return M_MediaImportResult.Fail(M_MediaImportFailure.InvalidImage, "The photo imported, but a gallery thumbnail could not be generated.");

            if (thumb.Length > thumbnailMaxBytes)
                return M_MediaImportResult.Fail(M_MediaImportFailure.TooLarge, "The thumbnail for this image is too large. Please choose a smaller or less detailed photo.");

            string mediaRelativePath = $"{M_MediaPaths.ImagesDirectory}/{sha256}.jpg";
            string thumbRelativePath = $"{M_MediaPaths.ThumbsDirectory}/{sha256}_{thumbnailLongEdge}.jpg";
            string mediaPath = mediaLibrary.ResolveRelativePath(mediaRelativePath);
            string thumbPath = mediaLibrary.ResolveRelativePath(thumbRelativePath);

            if (string.IsNullOrEmpty(mediaPath) || string.IsNullOrEmpty(thumbPath))
                return M_MediaImportResult.Fail(M_MediaImportFailure.IoError, "The media storage path could not be created safely.");

            WriteBytesAtomically(mediaPath, optimized);
            WriteBytesAtomically(thumbPath, thumb);

            M_MediaRecord record = new M_MediaRecord
            {
                mediaId = CreateMediaId(),
                kind = "image",
                mime = "image/jpeg",
                sha256 = sha256,
                relativePath = mediaRelativePath,
                thumbPath = thumbRelativePath,
                width = storedWidth,
                height = storedHeight,
                byteSize = optimized.Length,
                createdUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                displayName = M_MediaPaths.SafeDisplayName(originalFileName)
            };

            mediaLibrary.AddOrUpdateRecord(record);

            if (verboseLogging)
                Debug.Log($"[M_MediaImportController] Imported {record.displayName} as mediaId={record.mediaId}, sha={record.sha256}, bytes={record.byteSize}.");

            return M_MediaImportResult.Ok(record, mediaPath, thumbPath, duplicate: false, message: "Photo imported into your Quest gallery.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[M_MediaImportController] Import failed: {e}");
            return M_MediaImportResult.Fail(M_MediaImportFailure.Unknown, "The photo could not be imported. Please try another JPG or PNG.");
        }
        finally
        {
            Destroy(decoded);
        }
    }

    public bool TryImportReceivedImageToLibrary(string sourcePath, string displayName, out M_MediaImportResult result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            result = M_MediaImportResult.Fail(M_MediaImportFailure.IoError, "The received image file could not be found.");
            return false;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(sourcePath);
            result = ImportImageBytes(bytes, "image/jpeg", displayName);
            return result != null && result.Success;
        }
        catch (Exception e)
        {
            result = M_MediaImportResult.Fail(M_MediaImportFailure.IoError, $"Save failed: {e.Message}");
            return false;
        }
    }

    private void ResolveReferences()
    {
        if (mediaLibrary == null)
            mediaLibrary = GetComponent<M_MediaLibrary>();

        if (mediaLibrary == null)
            mediaLibrary = FindFirstObjectByType<M_MediaLibrary>();
    }

    private static string NormalizeMime(string mime, string fileName, byte[] bytes)
    {
        string lower = string.IsNullOrWhiteSpace(mime) ? string.Empty : mime.Trim().ToLowerInvariant();
        if (lower == "image/jpg")
            lower = "image/jpeg";

        if (lower == "image/jpeg" || lower == "image/png")
            return lower;

        if (bytes != null && bytes.Length >= 8)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xD8)
                return "image/jpeg";

            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return "image/png";
        }

        string extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        if (extension == ".jpg" || extension == ".jpeg")
            return "image/jpeg";

        if (extension == ".png")
            return "image/png";

        return lower;
    }

    private static void WriteBytesAtomically(string finalPath, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath));
        string tempPath = finalPath + ".tmp";
        File.WriteAllBytes(tempPath, bytes);

        if (File.Exists(finalPath))
            File.Delete(finalPath);

        File.Move(tempPath, finalPath);
    }

    private static string CreateMediaId()
    {
        return Guid.NewGuid().ToString("N").Substring(0, 16);
    }

    private static string FormatBytes(int bytes)
    {
        if (bytes >= 1024 * 1024)
            return $"{bytes / (1024f * 1024f):0.0} MB";

        if (bytes >= 1024)
            return $"{bytes / 1024f:0.0} KB";

        return $"{bytes} bytes";
    }
}
