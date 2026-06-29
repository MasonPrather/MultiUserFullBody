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
