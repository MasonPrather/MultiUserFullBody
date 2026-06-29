/*
 * Script Name: M_MediaManifestStore.cs
 * Description: Atomic manifest persistence for the local media library.
 * Project Role: Prevents app quits or headset power loss from leaving manifest.json half-written.
 */

using System;
using System.IO;
using UnityEngine;

public sealed class M_MediaManifestStore
{
    public const string ManifestFileName = "manifest.json";

    private readonly string _libraryRoot;
    private readonly string _manifestPath;

    public string ManifestPath => _manifestPath;

    public M_MediaManifestStore(string libraryRoot)
    {
        _libraryRoot = libraryRoot;
        _manifestPath = Path.Combine(_libraryRoot, ManifestFileName);
    }

    public M_MediaManifest Load()
    {
        try
        {
            Directory.CreateDirectory(_libraryRoot);

            if (!File.Exists(_manifestPath))
                return new M_MediaManifest();

            string json = File.ReadAllText(_manifestPath);
            if (string.IsNullOrWhiteSpace(json))
                return new M_MediaManifest();

            M_MediaManifest manifest = JsonUtility.FromJson<M_MediaManifest>(json);
            if (manifest == null)
                return new M_MediaManifest();

            if (manifest.records == null)
                manifest.records = new System.Collections.Generic.List<M_MediaRecord>();

            if (manifest.version <= 0)
                manifest.version = 1;

            return manifest;
        }
        catch (Exception e)
        {
            Debug.LogError($"[M_MediaManifestStore] Failed to load manifest '{_manifestPath}': {e.Message}");
            return new M_MediaManifest();
        }
    }

    public bool Save(M_MediaManifest manifest)
    {
        try
        {
            Directory.CreateDirectory(_libraryRoot);
            manifest ??= new M_MediaManifest();
            manifest.version = Mathf.Max(1, manifest.version);
            if (manifest.records == null)
                manifest.records = new System.Collections.Generic.List<M_MediaRecord>();

            string json = JsonUtility.ToJson(manifest, true);
            string tempPath = _manifestPath + ".tmp";
            string backupPath = _manifestPath + ".bak";

            File.WriteAllText(tempPath, json);

            // File.Replace is the most atomic option when the destination exists. Some Android filesystems can throw for
            // Replace, so the fallback still writes a complete temp file before swapping paths.
            if (File.Exists(_manifestPath))
            {
                try
                {
                    File.Replace(tempPath, _manifestPath, backupPath);
                }
                catch
                {
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);

                    File.Copy(_manifestPath, backupPath, true);
                    File.Delete(_manifestPath);
                    File.Move(tempPath, _manifestPath);
                }
            }
            else
            {
                File.Move(tempPath, _manifestPath);
            }

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[M_MediaManifestStore] Failed to save manifest '{_manifestPath}': {e.Message}");
            return false;
        }
    }
}
