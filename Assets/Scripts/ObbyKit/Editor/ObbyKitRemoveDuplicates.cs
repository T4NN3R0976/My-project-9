// =====================================================================================
//  ObbyKitRemoveDuplicates.cs  —  ObbyKit (Editor only)
// -------------------------------------------------------------------------------------
//  Tools ▸ ObbyKit ▸ Remove Duplicate Objects
//
//  Finds top-level objects that are EXACT copies stacked on top of each other, and
//  deletes the extra copies, keeping one of each. Two objects count as duplicates only
//  when ALL of these match:
//    • name (ignoring " (1)", " (2)" suffixes) and source prefab
//    • world position, rotation and scale
//    • every child: name, local position/rotation/scale, active state
//    • every component and its settings (e.g. a SpinningBeam's speed), and every
//      renderer's world bounds (so differently-shaped meshes never match)
//  Also:
//    • Lights: if a light had stacked copies, the one kept gets their combined intensity,
//      so the scene stays exactly as bright as before.
//    • The left-over inactive copy of the old player (no movement script) is removed. The
//      original old player (with PlayerMovement) is kept, because the character builder uses
//      it as a template.
//    • Checkpoints drop references to deleted flag copies.
//  Safety: saves a backup scene first (Assets/Scenes/<Scene>_BeforeDuplicateRemoval.unity),
//  everything is ONE Undo step (Ctrl+Z), and a report goes to ObbyKitDuplicateReport.txt.
// =====================================================================================

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ObbyKit.EditorTools
{
    public static class ObbyKitRemoveDuplicates
    {
        private static readonly string[] Protected = { "ObbyKit (generated)", "ObbyPlayer", "Third Person Camera" };

        [MenuItem("Tools/ObbyKit/Remove Duplicate Objects")]
        public static void Run()
        {
            Scene scene = SceneManager.GetActiveScene();
            var report = new StringBuilder();
            report.AppendLine($"ObbyKit duplicate removal — scene '{scene.path}'");

            // ---------------------------------------------------------------- Backup
            if (scene.isDirty) EditorSceneManager.SaveScene(scene);
            string backup = Path.Combine(Path.GetDirectoryName(scene.path) ?? "Assets",
                                         Path.GetFileNameWithoutExtension(scene.path) + "_BeforeDuplicateRemoval.unity").Replace('\\', '/');
            backup = AssetDatabase.GenerateUniqueAssetPath(backup);   // never overwrite an earlier backup
            AssetDatabase.CopyAsset(scene.path, backup);
            report.AppendLine($"Backup saved as {backup}");

            int rootsBefore = scene.GetRootGameObjects().Length;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Remove Duplicate Objects");

            GameObject[] roots = scene.GetRootGameObjects()
                .Where(r => !Protected.Contains(r.name) && r.GetComponentInChildren<Checkpoint>(true) == null
                            && r.GetComponentInChildren<VoidZone>(true) == null)
                .ToArray();

            // ---------------------------------------------------------------- Group exact copies
            var groups = new Dictionary<string, List<GameObject>>();
            foreach (var r in roots)
            {
                string sig = Signature(r);
                if (!groups.TryGetValue(sig, out var list)) groups[sig] = list = new List<GameObject>();
                list.Add(r);
            }

            int deleted = 0;
            var perName = new SortedDictionary<string, int>();
            foreach (var group in groups.Values.Where(g => g.Count > 1))
            {
                var ordered = group.OrderByDescending(g => g.activeInHierarchy)
                                   .ThenBy(g => g.transform.GetSiblingIndex()).ToList();
                GameObject keep = ordered[0];

                // Keep the scene equally bright: give the kept light the combined intensity.
                var keepLights = keep.GetComponentsInChildren<Light>(true);
                if (keepLights.Length > 0)
                {
                    for (int i = 0; i < keepLights.Length; i++)
                    {
                        float total = keepLights[i].intensity;
                        foreach (var dup in ordered.Skip(1))
                        {
                            var dl = dup.GetComponentsInChildren<Light>(true);
                            if (i < dl.Length && dl[i].enabled && dup.activeInHierarchy) total += dl[i].intensity;
                        }
                        if (!Mathf.Approximately(total, keepLights[i].intensity))
                        {
                            Undo.RecordObject(keepLights[i], "Combine light intensity");
                            report.AppendLine($"Light '{keep.name}': intensity {keepLights[i].intensity:0.###} → {total:0.###} (combined from {ordered.Count} stacked copies).");
                            keepLights[i].intensity = total;
                        }
                    }
                }

                foreach (var dup in ordered.Skip(1))
                {
                    string key = BaseName(dup.name);
                    perName[key] = perName.TryGetValue(key, out int n) ? n + 1 : 1;
                    Undo.DestroyObjectImmediate(dup);
                    deleted++;
                }
            }

            // ---------------------------------------------------------------- Left-over old player copy
            var oldPlayers = UnityEngine.Object.FindObjectsOfType<PlayerMovement>(true).Where(p => p.gameObject.scene == scene).ToList();
            foreach (var r in scene.GetRootGameObjects())
            {
                if (r == null || Protected.Contains(r.name) || r.activeSelf) continue;
                if (r.GetComponentInChildren<PlayerMovement>(true) != null || r.GetComponentInChildren<PlayerController>(true) != null) continue;
                bool isPlayerCopy = r.GetComponent("UniversalAnimationViewer") != null && BaseName(r.name) == "Player"
                                    && oldPlayers.Any(p => Vector3.Distance(p.transform.position, r.transform.position) < 0.5f);
                if (!isPlayerCopy) continue;
                report.AppendLine($"Removed left-over inactive old player copy '{r.name}'.");
                Undo.DestroyObjectImmediate(r);
                deleted++;
            }

            // ---------------------------------------------------------------- Tidy checkpoint references
            foreach (var cp in UnityEngine.Object.FindObjectsOfType<Checkpoint>(true))
            {
                var so = new SerializedObject(cp);
                var arr = so.FindProperty("flagRenderers");
                bool changed = false;
                for (int i = arr.arraySize - 1; i >= 0; i--)
                {
                    if (arr.GetArrayElementAtIndex(i).objectReferenceValue == null)
                    {
                        arr.GetArrayElementAtIndex(i).objectReferenceValue = null;
                        arr.DeleteArrayElementAtIndex(i);
                        changed = true;
                    }
                }
                if (changed) so.ApplyModifiedProperties();
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            report.AppendLine($"Deleted {deleted} duplicate objects in total:");
            foreach (var kv in perName) report.AppendLine($"  {kv.Key}: {kv.Value}");
            report.AppendLine($"Top-level objects before: {rootsBefore}, after: {scene.GetRootGameObjects().Length}.");

            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "ObbyKitDuplicateReport.txt");
            File.WriteAllText(path, report.ToString());
            Debug.Log("[ObbyKit Duplicates] " + report);
        }

        // ================================================================ Diagnostics (changes nothing)

        /// <summary>Lists objects that share a name and position but are NOT exact copies, and what differs.</summary>
        [MenuItem("Tools/ObbyKit/Explain Near-Duplicates (no changes)")]
        public static void Explain()
        {
            Scene scene = SceneManager.GetActiveScene();
            var report = new StringBuilder();
            report.AppendLine($"Near-duplicates in '{scene.path}' (same name + position, but not identical):");

            var roots = scene.GetRootGameObjects().Where(r => !Protected.Contains(r.name)).ToList();
            var byPlace = roots.GroupBy(r => BaseName(r.name) + V(r.transform.position));
            int count = 0;
            foreach (var place in byPlace.Where(g => g.Count() > 1))
            {
                var items = place.ToList();
                string[] first = Signature(items[0], true).Split('\n');
                foreach (var other in items.Skip(1))
                {
                    string[] lines = Signature(other, true).Split('\n');
                    if (string.Join("\n", lines) == string.Join("\n", first)) continue;
                    count++;
                    report.AppendLine($"\n'{items[0].name}' vs '{other.name}' at {V(other.transform.position)}:");
                    if (lines.Length != first.Length)
                        report.AppendLine($"  different number of child objects: {first.Length - 1} vs {lines.Length - 1}");
                    int shown = 0;
                    for (int i = 0; i < Mathf.Min(lines.Length, first.Length) && shown < 3; i++)
                    {
                        if (lines[i] == first[i]) continue;
                        report.AppendLine(Diff(first[i], lines[i]));
                        shown++;
                    }
                }
            }
            report.AppendLine($"\n{count} near-duplicate pairs.");
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "ObbyKitNearDuplicates.txt");
            File.WriteAllText(path, report.ToString());
            Debug.Log("[ObbyKit Duplicates] Near-duplicate report written to " + path);
        }

        /// <summary>Shows both lines around the first character where they differ.</summary>
        private static string Diff(string a, string b)
        {
            int i = 0;
            while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
            int start = Mathf.Max(0, i - 160);
            string Cut(string s) => s.Length <= start ? "" : s.Substring(start, Mathf.Min(320, s.Length - start));
            string path = a.Split(':')[0];
            return $"  in '{path}':\n    A: …{Cut(a)}…\n    B: …{Cut(b)}…";
        }

        // ================================================================ Signature

        private static string BaseName(string name)
        {
            string n = name;
            while (Regex.IsMatch(n, @"\s*\(\d+\)$")) n = Regex.Replace(n, @"\s*\(\d+\)$", "");
            return n;
        }

        private static string Signature(GameObject root, bool verbose = false)
        {
            var sb = new StringBuilder();
            sb.Append(BaseName(root.name)).Append('|');
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(root);
            sb.Append(source != null ? AssetDatabase.GetAssetPath(source) + "/" + source.name : "-").Append('|');
            sb.Append(root.activeSelf).Append('|');
            sb.Append(V(root.transform.position)).Append(V(root.transform.eulerAngles)).Append(V(root.transform.lossyScale)).Append('|');

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                sb.Append('\n').Append(t == root.transform ? "." : AnimationUtility.CalculateTransformPath(t, root.transform));
                sb.Append(':').Append(t.gameObject.activeSelf).Append(t.gameObject.layer);
                if (t != root.transform) sb.Append(V(t.localPosition)).Append(V(t.localEulerAngles)).Append(V(t.localScale));
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null) { sb.Append("[missing]"); continue; }
                    if (c is Transform) continue;
                    string json = EditorJsonUtility.ToJson(c);
                    json = Regex.Replace(json, "\"instanceID\":-?\\d+", "");
                    sb.Append('[').Append(c.GetType().Name).Append(verbose ? json : Hash128.Compute(json).ToString()).Append(']');
                    if (c is Renderer rend) sb.Append(V(rend.bounds.center)).Append(V(rend.bounds.size));
                }
            }
            return sb.ToString();
        }

        private static string V(Vector3 v)
        {
            float R(float f) => Mathf.Round(f * 100f) / 100f + 0f;   // + 0f turns -0 into 0
            return $"({R(v.x):0.00},{R(v.y):0.00},{R(v.z):0.00})";
        }
    }
}
