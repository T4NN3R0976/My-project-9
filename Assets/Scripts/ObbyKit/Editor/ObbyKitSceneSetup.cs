// =====================================================================================
//  ObbyKitSceneSetup.cs  —  ObbyKit (Editor only)
// -------------------------------------------------------------------------------------
//  One-click wiring for this project's obby scene:  Tools ▸ ObbyKit ▸ Set Up Scene
//
//  What it does (all in ONE Undo step: Edit ▸ Undo reverses everything except the new
//  layers, which are harmless):
//   1. Creates the layers Wall, MonkeyBar and Player (if they don't already exist).
//   2. Keeps one Player. Duplicate Players stacked in the same spot are DEACTIVATED
//      (not deleted). On the kept Player, the old PlayerMovement is DISABLED (not
//      removed) and ObbyKit.PlayerController is added, copying the old speeds, jump,
//      gravity and mouse settings. Control Scheme = MouseYaw, because the camera is a
//      child of the Player.
//   3. Under a new "ObbyKit (generated)" object it creates:
//        • CheckpointManager
//        • one Checkpoint trigger per flag location (duplicates merged), in course order,
//          each with a SpawnPoint facing the next flag. Level Number = index + 2, so the
//          5 flags start Levels 2..6. Level 3 (wallrun) and Level 5 (monkey bars) start
//          at the 2nd and 4th flags.
//        • a MonkeyBar trigger box along the overhead rung cylinders
//        • a VoidZone covering everything a few metres below the lowest part of the course
//   4. Puts the red run walls (platform_4x2x1_red) on the Wall layer, adding
//      MeshColliders if they have none.
//   5. Saves the scene and writes ObbyKitSetupReport.txt in the project folder.
//  Running it again rebuilds the generated objects instead of duplicating them.
// =====================================================================================

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ObbyKit.EditorTools
{
    public static class ObbyKitSceneSetup
    {
        private const string RootName = "ObbyKit (generated)";
        private const string FlagPrefix = "flag_";
        private const string WallPrefix = "platform_4x2x1_red";
        private const string RungPrefix = "Cylinder";

        [MenuItem("Tools/ObbyKit/Set Up Scene")]
        public static void SetUp()
        {
            var report = new StringBuilder();
            Scene scene = SceneManager.GetActiveScene();
            report.AppendLine($"ObbyKit setup — scene '{scene.path}'");

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("ObbyKit Scene Setup");

            // ---------------------------------------------------------------- 1. Layers
            int wallLayer = EnsureLayer("Wall");
            int barLayer = EnsureLayer("MonkeyBar");
            int playerLayer = EnsureLayer("Player");
            report.AppendLine($"Layers: Wall={wallLayer}, MonkeyBar={barLayer}, Player={playerLayer}");

            GameObject[] roots = scene.GetRootGameObjects();

            // Remove a previous run's generated objects.
            foreach (var old in roots.Where(r => r.name == RootName).ToList())
                Undo.DestroyObjectImmediate(old);
            roots = scene.GetRootGameObjects();

            // ---------------------------------------------------------------- 2. Player
            var movers = Object.FindObjectsOfType<PlayerMovement>(true);
            if (movers.Length == 0)
            {
                Debug.LogError("[ObbyKit Setup] No PlayerMovement found in the scene, so I can't tell which object is the Player.");
                return;
            }
            PlayerMovement keep = movers
                .OrderByDescending(m => m.gameObject.activeInHierarchy)
                .ThenByDescending(m => m.GetComponentsInChildren<AudioListener>(true).Any(a => a.enabled))
                .ThenBy(m => m.transform.GetSiblingIndex())
                .First();
            GameObject player = keep.gameObject;

            foreach (var other in movers)
            {
                if (other.gameObject == player || !other.gameObject.activeSelf) continue;
                Undo.RecordObject(other.gameObject, "Deactivate duplicate player");
                other.gameObject.SetActive(false);
                RecordPrefab(other.gameObject);
                report.AppendLine($"Deactivated duplicate player '{other.name}'.");
            }

            Undo.RecordObject(keep, "Disable PlayerMovement");
            keep.enabled = false;
            RecordPrefab(keep);

            var controller = player.GetComponent<CharacterController>();
            var pc = player.GetComponent<PlayerController>();
            if (pc == null) pc = Undo.AddComponent<PlayerController>(player);
            Camera cam = player.GetComponentInChildren<Camera>(true);

            foreach (Transform t in player.GetComponentsInChildren<Transform>(true))
            {
                Undo.RecordObject(t.gameObject, "Set Player layer");
                t.gameObject.layer = playerLayer;
                RecordPrefab(t.gameObject);
            }
            report.AppendLine($"Player: kept '{player.name}', PlayerMovement disabled, PlayerController added, " +
                              $"camera = {(cam ? cam.name : "none")}. CharacterController height {controller.height}, center {controller.center}.");

            var anim = player.GetComponentInChildren<Animator>(true);
            if (anim != null && anim.runtimeAnimatorController != null)
            {
                if (player.GetComponent<PlayerAnimationController>() == null)
                    Undo.AddComponent<PlayerAnimationController>(player);
                report.AppendLine($"Animator found on '{anim.name}' (controller '{anim.runtimeAnimatorController.name}'), so PlayerAnimationController was added. " +
                                  "Parameters: " + string.Join(", ", anim.parameters.Select(p => $"{p.name}({p.type})")));
            }
            else
            {
                report.AppendLine("No Animator with a controller on the Player, so PlayerAnimationController was NOT added.");
            }

            // ---------------------------------------------------------------- Generated root
            var root = new GameObject(RootName);

            if (Object.FindObjectsOfType<CheckpointManager>(true).Length == 0)
            {
                var mgr = new GameObject("CheckpointManager");
                mgr.transform.SetParent(root.transform, false);
                mgr.AddComponent<CheckpointManager>();
            }

            Physics.SyncTransforms();
            int groundMask = ~(1 << playerLayer) & ~(1 << barLayer);

            // ---------------------------------------------------------------- 3a. Checkpoints
            var flagGroups = GroupByPosition(roots.Where(r => r.activeInHierarchy && r.name.StartsWith(FlagPrefix)), 1f);
            var orderedFlags = OrderAlongCourse(flagGroups, player.transform.position);
            report.AppendLine($"Flags: {flagGroups.Sum(g => g.Count)} objects at {orderedFlags.Count} locations.");

            for (int i = 0; i < orderedFlags.Count; i++)
            {
                Vector3 pos = orderedFlags[i][0].transform.position;
                Vector3 dir;
                if (i + 1 < orderedFlags.Count) dir = orderedFlags[i + 1][0].transform.position - pos;
                else if (i > 0) dir = pos - orderedFlags[i - 1][0].transform.position;
                else dir = player.transform.forward;
                dir.y = 0f;
                dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;

                int level = i + 2;
                var cpGo = new GameObject($"Checkpoint {i} (Level {level})");
                cpGo.transform.SetParent(root.transform, false);
                cpGo.transform.position = pos;
                cpGo.transform.rotation = Quaternion.LookRotation(dir);

                var box = cpGo.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(7f, 6f, 7f);
                box.center = new Vector3(0f, 2f, 0f);

                var spawn = new GameObject("SpawnPoint");
                spawn.transform.SetParent(cpGo.transform, false);
                spawn.transform.position = FindGround(pos + dir * 1.5f, pos, groundMask);
                spawn.transform.rotation = Quaternion.LookRotation(dir);

                var cp = cpGo.AddComponent<Checkpoint>();
                var so = new SerializedObject(cp);
                so.FindProperty("index").intValue = i;
                so.FindProperty("levelNumber").intValue = level;
                so.FindProperty("spawnPoint").objectReferenceValue = spawn.transform;
                var renderers = orderedFlags[i].SelectMany(f => f.GetComponentsInChildren<Renderer>(true)).ToArray();
                var rProp = so.FindProperty("flagRenderers");
                rProp.arraySize = renderers.Length;
                for (int r = 0; r < renderers.Length; r++) rProp.GetArrayElementAtIndex(r).objectReferenceValue = renderers[r];
                so.ApplyModifiedPropertiesWithoutUndo();

                report.AppendLine($"  Checkpoint {i}: flag at {Fmt(pos)}, spawn {Fmt(spawn.transform.position)}, Level {level}, {renderers.Length} renderers recoloured.");
            }

            // ---------------------------------------------------------------- 3b. Monkey bars
            float hangOffset = -1f;
            var rungGroup = FindRungRow(roots, barLayer);
            if (rungGroup.Count > 0)
            {
                Bounds b = EncapsulateRenderers(rungGroup);
                var barGo = new GameObject("MonkeyBars Trigger");
                barGo.transform.SetParent(root.transform, false);
                barGo.transform.position = b.center;
                barGo.layer = barLayer;
                var bc = barGo.AddComponent<BoxCollider>();
                bc.isTrigger = true;
                bc.size = b.size + new Vector3(0.2f, 0.3f, 0.6f);

                // Keep the head just under the rungs: pivot→top of capsule + rung half-thickness + a gap.
                float scaleY = player.transform.lossyScale.y;
                float pivotToTop = (controller.center.y + controller.height * 0.5f) * scaleY;
                hangOffset = pivotToTop + b.extents.y + 0.1f;

                report.AppendLine($"Monkey bars: {rungGroup.Count} rung objects, trigger center {Fmt(b.center)} size {Fmt(bc.size)}, hangOffset {hangOffset:0.00}.");
                // Clearance report: how far below each end of the bar the nearest floor is.
                bool alongZ = b.size.z >= b.size.x;
                Vector3 endA = alongZ ? new Vector3(b.center.x, b.center.y, b.min.z) : new Vector3(b.min.x, b.center.y, b.center.z);
                Vector3 endB = alongZ ? new Vector3(b.center.x, b.center.y, b.max.z) : new Vector3(b.max.x, b.center.y, b.center.z);
                foreach (var (label, end) in new[] { ("end A", endA), ("end B", endB) })
                {
                    for (float step = 0f; step <= 4f; step += 2f)
                    {
                        Vector3 outward = (end - b.center).normalized * step;
                        float? floor = FloorBelow(end + outward, groundMask, rungGroup);
                        report.AppendLine($"    floor {step:0}m beyond {label} {Fmt(end + outward)}: " +
                                          (floor.HasValue ? $"y={floor.Value:0.00} (bar is {b.center.y - floor.Value:0.00} m above it)" : "none"));
                    }
                }
            }
            else
            {
                report.AppendLine("Monkey bars: no row of horizontal rung cylinders found, so no trigger was created.");
            }

            // ---------------------------------------------------------------- 4. Walls
            var wallObjects = roots.Where(r => r.activeInHierarchy && r.name.StartsWith(WallPrefix)).ToList();
            var wallGroups = GroupByPosition(wallObjects, 0.5f);
            int addedColliders = 0;
            foreach (var w in wallObjects)
            {
                foreach (Transform t in w.GetComponentsInChildren<Transform>(true))
                {
                    Undo.RecordObject(t.gameObject, "Set Wall layer");
                    t.gameObject.layer = wallLayer;
                    RecordPrefab(t.gameObject);
                }
                if (w.GetComponentsInChildren<Collider>(true).Length == 0)
                {
                    foreach (var mf in w.GetComponentsInChildren<MeshFilter>(true))
                    {
                        Undo.AddComponent<MeshCollider>(mf.gameObject);
                        addedColliders++;
                    }
                }
            }
            report.AppendLine($"Walls: {wallObjects.Count} objects at {wallGroups.Count} locations put on the Wall layer, {addedColliders} MeshColliders added.");
            foreach (var g in wallGroups)
            {
                Bounds wb = EncapsulateRenderers(g);
                report.AppendLine($"    wall at {Fmt(g[0].transform.position)} size {Fmt(wb.size)}");
            }

            // ---------------------------------------------------------------- 5. Void zone
            var courseObjects = roots.Where(r => r.activeInHierarchy &&
                (r.name.StartsWith("platform_") || r.name.StartsWith(FlagPrefix) || r.name.StartsWith("spring_pad") ||
                 r.name.StartsWith("barrier") || r.name.StartsWith(RungPrefix))).ToList();
            Bounds course = EncapsulateRenderers(courseObjects);
            course.Encapsulate(player.transform.position);
            float topY = course.min.y - 3f;
            var vz = new GameObject("VoidZone");
            vz.transform.SetParent(root.transform, false);
            vz.transform.position = new Vector3(course.center.x, topY - 10f, course.center.z);
            var vbox = vz.AddComponent<BoxCollider>();
            vbox.isTrigger = true;
            vbox.size = new Vector3(course.size.x + 160f, 20f, course.size.z + 160f);
            vz.AddComponent<VoidZone>();
            report.AppendLine($"VoidZone: course bounds {Fmt(course.min)} → {Fmt(course.max)}. Kill height y={topY:0.00}.");

            // ---------------------------------------------------------------- PlayerController settings
            var pso = new SerializedObject(pc);
            pso.FindProperty("currentLevel").intValue = 1;
            pso.FindProperty("controlScheme").enumValueIndex = (int)ControlScheme.MouseYaw;
            pso.FindProperty("cameraTransform").objectReferenceValue = cam != null ? cam.transform : null;
            pso.FindProperty("mouseSensitivity").floatValue = keep.mouseSensitivity;
            pso.FindProperty("minLookAngle").floatValue = keep.minLookAngle;
            pso.FindProperty("maxLookAngle").floatValue = keep.maxLookAngle;
            pso.FindProperty("invertY").boolValue = keep.invertY;
            pso.FindProperty("walkSpeed").floatValue = keep.walkSpeed;
            pso.FindProperty("sprintSpeed").floatValue = keep.sprintSpeed;
            pso.FindProperty("jumpHeight").floatValue = keep.jumpHeight;
            pso.FindProperty("gravity").floatValue = keep.gravity;
            pso.FindProperty("coyoteTime").floatValue = keep.coyoteTime;
            pso.FindProperty("jumpBufferTime").floatValue = keep.jumpBufferTime;
            pso.FindProperty("groundKnockbackFade").floatValue = keep.groundKnockbackFade;
            pso.FindProperty("airKnockbackFade").floatValue = keep.airKnockbackFade;
            pso.FindProperty("wallLayer").intValue = 1 << wallLayer;
            pso.FindProperty("groundLayer").intValue = groundMask;
            pso.FindProperty("monkeyBarLayer").intValue = 1 << barLayer;
            pso.FindProperty("grabRadius").floatValue = 0.8f;
            // Tuned for this course: run walls ~19 m long with ~12.7 m between opposite faces.
            pso.FindProperty("wallRunMaxDuration").floatValue = 2.5f;
            pso.FindProperty("wallJumpAwayForce").floatValue = 14f;
            pso.FindProperty("wallJumpUpForce").floatValue = 8f;
            pso.FindProperty("wallJumpAirLock").floatValue = 0.5f;
            if (hangOffset > 0f) pso.FindProperty("hangOffset").floatValue = hangOffset;
            pso.ApplyModifiedProperties();

            // ---------------------------------------------------------------- Finish
            Undo.RegisterCreatedObjectUndo(root, "Create ObbyKit objects");
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            string reportPath = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "ObbyKitSetupReport.txt");
            File.WriteAllText(reportPath, report.ToString());
            Debug.Log("[ObbyKit Setup] Done. Scene saved. Report written to " + reportPath + "\n" + report);
        }

        // ================================================================ Helpers

        private static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 8; i < 32; i++)
            {
                var p = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(p.stringValue))
                {
                    p.stringValue = name;
                    tagManager.ApplyModifiedPropertiesWithoutUndo();
                    return i;
                }
            }
            throw new System.Exception($"No free layer slot for '{name}'.");
        }

        private static void RecordPrefab(Object o)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(o)) PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        }

        private static List<List<GameObject>> GroupByPosition(IEnumerable<GameObject> objects, float tolerance)
        {
            var groups = new List<List<GameObject>>();
            foreach (var o in objects)
            {
                var g = groups.FirstOrDefault(x => Vector3.Distance(x[0].transform.position, o.transform.position) <= tolerance);
                if (g != null) g.Add(o); else groups.Add(new List<GameObject> { o });
            }
            return groups;
        }

        /// <summary>Nearest-neighbour walk from the player's start (horizontal distance).</summary>
        private static List<List<GameObject>> OrderAlongCourse(List<List<GameObject>> groups, Vector3 start)
        {
            var remaining = new List<List<GameObject>>(groups);
            var ordered = new List<List<GameObject>>();
            Vector3 current = start;
            while (remaining.Count > 0)
            {
                var next = remaining.OrderBy(g => Flat(g[0].transform.position - current).sqrMagnitude).First();
                ordered.Add(next);
                remaining.Remove(next);
                current = next[0].transform.position;
            }
            return ordered;
        }

        /// <summary>Largest straight row (4+ positions) of horizontal cylinders: the monkey bar rungs.</summary>
        private static List<GameObject> FindRungRow(GameObject[] roots, int barLayer)
        {
            var candidates = roots
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Where(t => t.gameObject.activeInHierarchy && t.name.StartsWith(RungPrefix) && t.GetComponent<Renderer>() != null)
                .Where(t => Mathf.Abs(Vector3.Dot(t.up, Vector3.up)) < 0.3f)
                .ToList();

            var rows = candidates.GroupBy(t =>
            {
                bool axisX = Mathf.Abs(t.up.x) > Mathf.Abs(t.up.z);
                // Rungs lying along X form a row running along Z (same X), and the other way round.
                float across = axisX ? t.position.x : t.position.z;
                return (axisX, Mathf.Round(across), Mathf.Round(t.position.y / 2f));
            });

            var best = rows
                .Select(g => g.Select(t => t.gameObject).ToList())
                .Where(g => GroupByPosition(g, 0.5f).Count >= 4)
                .OrderByDescending(g => g.Count)
                .FirstOrDefault();
            return best ?? new List<GameObject>();
        }

        private static Bounds EncapsulateRenderers(IEnumerable<GameObject> objects)
        {
            bool has = false;
            var b = new Bounds();
            foreach (var o in objects)
                foreach (var r in o.GetComponentsInChildren<Renderer>(true))
                {
                    if (!has) { b = r.bounds; has = true; }
                    else b.Encapsulate(r.bounds);
                }
            return b;
        }

        private static Vector3 FindGround(Vector3 point, Vector3 fallback, int mask)
        {
            if (Physics.Raycast(point + Vector3.up * 4f, Vector3.down, out RaycastHit hit, 12f, mask, QueryTriggerInteraction.Ignore)
                && Mathf.Abs(hit.point.y - fallback.y) < 2f)
                return hit.point;
            return fallback;
        }

        private static float? FloorBelow(Vector3 from, int mask, List<GameObject> ignore)
        {
            var hits = Physics.RaycastAll(from, Vector3.down, 60f, mask, QueryTriggerInteraction.Ignore)
                              .OrderBy(h => h.distance);
            foreach (var h in hits)
            {
                if (ignore.Any(g => h.collider.transform.IsChildOf(g.transform))) continue;
                if (h.collider.name.StartsWith(RungPrefix)) continue;
                return h.point.y;
            }
            return null;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
        private static string Fmt(Vector3 v) => $"({v.x:0.0}, {v.y:0.0}, {v.z:0.0})";
    }
}
