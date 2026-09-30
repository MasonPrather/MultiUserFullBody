/*
 * Script Name: M_MediaModels.cs
 * Description: Shared data contracts for the local-first media library and lobby sharing systems.
 * Project Role: Keeps manifest, import, and cache records in one small, Unity-serializable place.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

[Serializable]
public class M_MediaManifest
{
    public int version = 1;
    public List<M_MediaRecord> records = new List<M_MediaRecord>();
}

[Serializable]
public class M_MediaRecord
{
    public string mediaId;
    public string kind;
    public string mime;
    public string sha256;
    public string relativePath;
    public string thumbPath;
    public int width;
    public int height;
    public int byteSize;
    public long createdUnixMs;
    public string displayName;
}

public enum M_MediaImportFailure
{
    None,
    EmptyInput,
    BadPairingCode,
    UnsupportedType,
    InvalidImage,
    InvalidVideo,
    TooLarge,
    IoError,
    Duplicate,
    Unknown
}

public sealed class M_MediaImportResult
{
    public bool Success;
    public bool Duplicate;
    public M_MediaImportFailure Failure;
    public string Message;
    public M_MediaRecord Record;
    public string FullPath;
    public string ThumbnailPath;

    public static M_MediaImportResult Ok(M_MediaRecord record, string fullPath, string thumbnailPath, bool duplicate, string message)
    {
        return new M_MediaImportResult
        {
            Success = true,
            Duplicate = duplicate,
            Failure = duplicate ? M_MediaImportFailure.Duplicate : M_MediaImportFailure.None,
            Message = message,
            Record = record,
            FullPath = fullPath,
            ThumbnailPath = thumbnailPath
        };
    }

    public static M_MediaImportResult Fail(M_MediaImportFailure failure, string message)
    {
        return new M_MediaImportResult
        {
            Success = false,
            Duplicate = false,
            Failure = failure,
            Message = message
        };
    }
}

public static class M_MediaPaths
{
    public const string LibraryRelativeRoot = "MURPM/MediaLibrary";
    public const string ImagesDirectory = "images";
    public const string VideosDirectory = "videos";
    public const string ThumbsDirectory = "thumbs";
    public const string IncomingDirectory = "incoming/temp_uploads";
    public const string SessionCacheDirectory = "session_cache";

    private static readonly Regex UnsafeFilenameChars = new Regex(@"[^\w\-. ]+", RegexOptions.Compiled);

    public static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return string.Empty;

        return relativePath.Replace('\\', '/').TrimStart('/');
    }

    public static bool IsSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        string normalized = NormalizeRelativePath(relativePath);
        if (Path.IsPathRooted(normalized))
            return false;

        string[] parts = normalized.Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(parts[i]) || parts[i] == "." || parts[i] == "..")
                return false;
        }

        return true;
    }

    public static string CombineUnderRoot(string root, string relativePath)
    {
        string normalized = NormalizeRelativePath(relativePath);
        if (!IsSafeRelativePath(normalized))
            return null;

        string fullRoot = Path.GetFullPath(root);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, normalized));

        // Android paths are case-sensitive, macOS project paths often are not. OrdinalIgnoreCase keeps editor checks friendly
        // while still preventing ../ traversal from exposing arbitrary files through upload/thumbnail routes.
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            return null;

        return fullPath;
    }

    public static string SafeDisplayName(string originalName)
    {
        if (string.IsNullOrWhiteSpace(originalName))
            return "Untitled memory";

        string withoutPath = Path.GetFileNameWithoutExtension(originalName);
        string cleaned = UnsafeFilenameChars.Replace(withoutPath, " ").Trim();
        while (cleaned.Contains("  "))
            cleaned = cleaned.Replace("  ", " ");

        if (cleaned.Length == 0)
            cleaned = "Untitled memory";

        return cleaned.Length > 64 ? cleaned.Substring(0, 64) : cleaned;
    }

    public static string SafeIdFragment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string cleaned = UnsafeFilenameChars.Replace(value, string.Empty);
        return cleaned.Length > 128 ? cleaned.Substring(0, 128) : cleaned;
    }
}

public static class M_MediaTypeUtility
{
    public const string KindImage = "image";
    public const string KindVideo = "video";

    public static string NormalizeMime(string mime, string fileName, byte[] bytes)
    {
        string lower = string.IsNullOrWhiteSpace(mime) ? string.Empty : mime.Trim().ToLowerInvariant();
        int semicolon = lower.IndexOf(';');
        if (semicolon >= 0)
            lower = lower.Substring(0, semicolon).Trim();

        if (lower == "image/jpg")
            lower = "image/jpeg";

        if (IsSupportedImageMime(lower) || IsSupportedVideoMime(lower))
            return lower;

        if (bytes != null)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return "image/jpeg";

            if (bytes.Length >= 8
                && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
                return "image/png";

            if (LooksLikeIsoBaseMediaFile(bytes))
                return GuessIsoBaseMediaMime(fileName);

            if (bytes.Length >= 4 && bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3)
                return "video/webm";
        }

        string extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        switch (extension)
        {
            case ".jpg":
            case ".jpeg":
                return "image/jpeg";
            case ".png":
                return "image/png";
            case ".mp4":
                return "video/mp4";
            case ".m4v":
                return "video/x-m4v";
            case ".mov":
                return "video/quicktime";
            case ".webm":
                return "video/webm";
            default:
                return lower;
        }
    }

    public static bool IsSupportedImageMime(string mime)
    {
        return string.Equals(mime, "image/jpeg", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mime, "image/png", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSupportedVideoMime(string mime)
    {
        return string.Equals(mime, "video/mp4", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mime, "video/quicktime", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mime, "video/x-m4v", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mime, "video/webm", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsVideoKind(string kind)
    {
        return string.Equals(kind, KindVideo, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsVideoMime(string mime)
    {
        return !string.IsNullOrWhiteSpace(mime)
               && mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsImageKind(string kind)
    {
        return string.Equals(kind, KindImage, StringComparison.OrdinalIgnoreCase);
    }

    public static string KindForMime(string mime)
    {
        return IsVideoMime(mime) ? KindVideo : KindImage;
    }

    public static string ExtensionForMime(string mime, string originalFileName = null)
    {
        string extension = Path.GetExtension(originalFileName)?.ToLowerInvariant();
        if (IsSafeKnownExtension(extension))
            return extension == ".jpeg" ? ".jpg" : extension;

        string normalized = NormalizeMime(mime, originalFileName, null);
        switch (normalized)
        {
            case "image/png":
                return ".png";
            case "video/quicktime":
                return ".mov";
            case "video/x-m4v":
                return ".m4v";
            case "video/webm":
                return ".webm";
            case "video/mp4":
                return ".mp4";
            default:
                return ".jpg";
        }
    }

    public static string DisplayNoun(string kindOrMime)
    {
        return IsVideoKind(kindOrMime) || IsVideoMime(kindOrMime) ? "video" : "photo";
    }

    private static bool IsSafeKnownExtension(string extension)
    {
        switch (extension)
        {
            case ".jpg":
            case ".jpeg":
            case ".png":
            case ".mp4":
            case ".m4v":
            case ".mov":
            case ".webm":
                return true;
            default:
                return false;
        }
    }

    private static bool LooksLikeIsoBaseMediaFile(byte[] bytes)
    {
        return bytes.Length >= 12
               && bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70;
    }

    private static string GuessIsoBaseMediaMime(string fileName)
    {
        string extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        if (extension == ".mov")
            return "video/quicktime";

        if (extension == ".m4v")
            return "video/x-m4v";

        return "video/mp4";
    }
}

public static class M_MediaHashUtility
{
    public static string Sha256Hex(byte[] bytes)
    {
        if (bytes == null)
            return string.Empty;

        using SHA256 sha = SHA256.Create();
        return ToHex(sha.ComputeHash(bytes));
    }

    public static string Sha256HexForFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return string.Empty;

        using SHA256 sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return ToHex(sha.ComputeHash(stream));
    }

    private static string ToHex(byte[] hash)
    {
        char[] chars = new char[hash.Length * 2];
        const string hex = "0123456789abcdef";
        for (int i = 0; i < hash.Length; i++)
        {
            chars[i * 2] = hex[hash[i] >> 4];
            chars[i * 2 + 1] = hex[hash[i] & 0xF];
        }

        return new string(chars);
    }
}
