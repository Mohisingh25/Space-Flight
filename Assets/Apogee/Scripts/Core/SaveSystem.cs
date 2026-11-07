using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Apogee.Rocket;
using UnityEngine;

namespace Apogee.Core
{
    public class BlueprintInfo
    {
        public string path;
        public string name;
        public int partCount;
        public float mass;
        public DateTime savedAt;
    }

    /// <summary>
    /// Rocket blueprints and autosave on local storage. Writes go to a temp file first and are then
    /// swapped in, so a crash mid-write never corrupts an existing save.
    /// </summary>
    public static class SaveSystem
    {
        static string Root { get { return Application.persistentDataPath; } }
        public static string BlueprintDir { get { return Path.Combine(Root, "Blueprints"); } }
        static string AutosavePath { get { return Path.Combine(Root, "Autosave", "builder.json"); } }

        public static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) name = "Untitled";
            var sb = new StringBuilder();
            foreach (char c in name.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '(' || c == ')') sb.Append(c);
                else sb.Append('_');
            }
            string s = sb.ToString().Trim();
            if (s.Length > 60) s = s.Substring(0, 60);
            return s.Length == 0 ? "Untitled" : s;
        }

        public static void WriteAtomic(string path, string contents)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, contents, Encoding.UTF8);
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); return; }
                catch (Exception) { /* some file systems lack replace; fall through */ }
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            else File.Move(tmp, path);
        }

        public static string PathFor(string rocketName)
        {
            return Path.Combine(BlueprintDir, SafeFileName(rocketName) + ".json");
        }

        public static bool Exists(string rocketName) { return File.Exists(PathFor(rocketName)); }

        public static bool SaveBlueprint(RocketBlueprint bp, out string error)
        {
            error = null;
            try
            {
                var copy = bp.DeepCopy();
                copy.savedAtTicks = DateTime.UtcNow.Ticks;
                WriteAtomic(PathFor(bp.name), copy.ToJson(true));
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogError("Save failed: " + e);
                return false;
            }
        }

        public static RocketBlueprint LoadBlueprint(string path)
        {
            try { return RocketBlueprint.FromJson(File.ReadAllText(path)); }
            catch (Exception e) { Debug.LogError("Load failed: " + e); return null; }
        }

        public static bool Delete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); return true; }
            catch (Exception e) { Debug.LogError("Delete failed: " + e); return false; }
        }

        public static List<BlueprintInfo> List()
        {
            var list = new List<BlueprintInfo>();
            try
            {
                if (!Directory.Exists(BlueprintDir)) return list;
                foreach (var f in Directory.GetFiles(BlueprintDir, "*.json"))
                {
                    var bp = LoadBlueprint(f);
                    if (bp == null) continue;
                    float mass = 0f;
                    foreach (var p in bp.parts) mass += PartMass.Wet(p);
                    list.Add(new BlueprintInfo
                    {
                        path = f, name = bp.name, partCount = bp.parts.Count, mass = mass,
                        savedAt = bp.savedAtTicks > 0 ? new DateTime(bp.savedAtTicks, DateTimeKind.Utc).ToLocalTime() : File.GetLastWriteTime(f)
                    });
                }
            }
            catch (Exception e) { Debug.LogError("Listing saves failed: " + e); }
            list.Sort((a, b) => b.savedAt.CompareTo(a.savedAt));
            return list;
        }

        public static void Autosave(RocketBlueprint bp)
        {
            try { WriteAtomic(AutosavePath, bp.ToJson()); }
            catch (Exception e) { Debug.LogWarning("Autosave failed: " + e.Message); }
        }

        public static RocketBlueprint LoadAutosave()
        {
            try { return File.Exists(AutosavePath) ? RocketBlueprint.FromJson(File.ReadAllText(AutosavePath)) : null; }
            catch (Exception e) { Debug.LogWarning("Autosave unreadable: " + e.Message); return null; }
        }

        public static bool HasAutosave()
        {
            var bp = LoadAutosave();
            return bp != null && !bp.IsEmpty;
        }
    }
}
