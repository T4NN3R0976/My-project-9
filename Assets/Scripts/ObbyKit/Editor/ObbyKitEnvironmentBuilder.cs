// =====================================================================================
//  ObbyKitEnvironmentBuilder.cs  —  ObbyKit (Editor only)
// -------------------------------------------------------------------------------------
//  Tools ▸ ObbyKit ▸ Build Hazards and Environment
//
//  Builds, in one Undo step (a backup scene is saved first):
//   HAZARDS ("ObbyKit Hazards")
//    • Level 1: a glowing laser across the middle of each blue spring, waist-low, between
//      two emitter posts. Touch it and you go back to your checkpoint (LaserHazard).
//    • Level 2: a padded spinning sweeper arm on each brown pole, at hip height, that knocks
//      you off the spring (SpinningBeam). The three poles differ in speed, direction and arms.
//   ENVIRONMENT ("ObbyKit Environment")
//    • Animated cartoon ocean (ObbyKit/CartoonWater) with foam rings around everything
//      standing in the water, and a splash when you fall in (ObbyKitOcean).
//    • Cartoon sky (ObbyKit/CartoonSky), distant low-poly tropical islands with trees, and
//      big puffy clouds drifting slowly around the course.
//    • Lighting: warm sun with soft shadows, sky/ground ambient, horizon fog, and a
//      post-processing profile (bloom so the lasers glow, extra colour pop, soft vignette).
//    • Moves the VoidZone up so you respawn right after hitting the water.
//  Running it again rebuilds everything instead of duplicating it.
// =====================================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ObbyKit.EditorTools
{
    public static class ObbyKitEnvironmentBuilder
    {
        private const string HazardsRoot = "ObbyKit Hazards";
        private const string EnvRoot = "ObbyKit Environment";
        private const string EnvDir = "Assets/ObbyKit/Environment";
        private const string MatDir = EnvDir + "/Materials";
        private const string MeshDir = EnvDir + "/Meshes";
        private const float WaterLevel = 0.6f;

        private static readonly Color Horizon = new Color(0.68f, 0.89f, 1.0f);

        private class Mats
        {
            public Material LaserCore, LaserMid, LaserGlow, DarkMetal, Steel, SteelLight, Hazard, Padding, PaddingStripe, EndCap;
            public Material Water, FoamBox, FoamRound, Splash, Sky;
            public Material Sand, Grass, Rock, Trunk, Leaves, LeavesDark, Cloud;
        }

        [MenuItem("Tools/ObbyKit/Build Hazards and Environment")]
        public static void Build()
        {
            var report = new StringBuilder();
            Scene scene = SceneManager.GetActiveScene();
            report.AppendLine($"ObbyKit hazards & environment — scene '{scene.path}'");

            // ---------------------------------------------------------------- Backup
            if (scene.isDirty) EditorSceneManager.SaveScene(scene);
            // Keep the FIRST backup (the scene before any environment was added); don't pile up copies on re-runs.
            string backup = Path.Combine(Path.GetDirectoryName(scene.path) ?? "Assets",
                                         Path.GetFileNameWithoutExtension(scene.path) + "_BeforeEnvironment.unity").Replace('\\', '/');
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(backup) == null) AssetDatabase.CopyAsset(scene.path, backup);
            report.AppendLine($"Backup: {backup}");

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Hazards and Environment");

            foreach (var old in scene.GetRootGameObjects().Where(g => g.name == HazardsRoot || g.name == EnvRoot).ToList())
                Undo.DestroyObjectImmediate(old);

            EnsureFolder("Assets", "ObbyKit");
            EnsureFolder("Assets/ObbyKit", "Environment");
            EnsureFolder(EnvDir, "Materials");
            EnsureFolder(EnvDir, "Meshes");

            Mats m = MakeMaterials(report);
            if (m == null) return;

            GameObject[] roots = scene.GetRootGameObjects();
            Bounds course = CourseBounds(roots);
            report.AppendLine($"Course bounds {F(course.min)} → {F(course.max)}");

            var hazards = new GameObject(HazardsRoot);
            var env = new GameObject(EnvRoot);

            BuildLasers(roots, hazards.transform, m, report);
            BuildSpinners(roots, hazards.transform, m, report);
            BuildOcean(env.transform, course, m, report);
            BuildFoamRings(roots, env.transform, m, report);
            BuildIslands(env.transform, course, m, report);
            BuildClouds(env.transform, course, m, report);
            SetupSkyAndLighting(m, report);
            SetupPostProcessing(roots, report);
            AdjustVoidZone(report);

            Undo.RegisterCreatedObjectUndo(hazards, "Create hazards");
            Undo.RegisterCreatedObjectUndo(env, "Create environment");
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "ObbyKitEnvironmentReport.txt");
            File.WriteAllText(path, report.ToString());
            Debug.Log("[ObbyKit Environment] Done. Scene saved.\n" + report);
        }

        // ================================================================ Course analysis

        private static Bounds CourseBounds(GameObject[] roots)
        {
            var objs = roots.Where(r => r.activeInHierarchy &&
                (r.name.StartsWith("platform_") || r.name.StartsWith("flag_") || r.name.StartsWith("spring_pad") ||
                 r.name.StartsWith("barrier") || r.name.StartsWith("Cylinder")));
            return Encapsulate(objs);
        }

        private static Vector3 PlayerStart()
        {
            var pc = Object.FindObjectsOfType<PlayerController>().FirstOrDefault(p => p.isActiveAndEnabled);
            return pc != null ? pc.transform.position : Vector3.zero;
        }

        private static Vector3? CheckpointPosition(int index)
        {
            var cp = Object.FindObjectsOfType<Checkpoint>(true).FirstOrDefault(c => c.Index == index);
            return cp != null ? cp.transform.position : (Vector3?)null;
        }

        private static float FlatDist(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        // ================================================================ Level 1 lasers

        private static void BuildLasers(GameObject[] roots, Transform parent, Mats m, StringBuilder report)
        {
            Vector3 start = PlayerStart();
            Vector3? cp0 = CheckpointPosition(0);
            if (cp0 == null) { report.AppendLine("Lasers: no Checkpoint 0 found — skipped."); return; }

            var pads = roots.Where(r => r.activeInHierarchy && r.name.StartsWith("spring_pad")).ToList();
            float cpDist = FlatDist(start, cp0.Value);
            var level1 = pads.Where(p => FlatDist(start, p.transform.position) < cpDist)
                             .OrderBy(p => FlatDist(start, p.transform.position)).ToList();

            var waypoints = new List<Vector3> { start };
            waypoints.AddRange(level1.Select(p => p.transform.position));
            waypoints.Add(cp0.Value);

            var group = new GameObject("Level 1 Lasers");
            group.transform.SetParent(parent, false);

            for (int i = 0; i < level1.Count; i++)
            {
                Bounds pb = Encapsulate(new[] { level1[i] });
                Vector3 travel = waypoints[i + 2] - waypoints[i];
                travel.y = 0f;
                travel = travel.sqrMagnitude > 0.01f ? travel.normalized : Vector3.forward;

                float radius = Mathf.Min(pb.extents.x, pb.extents.z);
                float top = pb.max.y;
                const float beamHeight = 0.6f;

                var root = new GameObject($"Laser {i + 1}");
                root.transform.SetParent(group.transform, false);
                root.transform.SetPositionAndRotation(new Vector3(pb.center.x, top, pb.center.z), Quaternion.LookRotation(travel));

                float postX = radius - 0.45f;
                foreach (float side in new[] { -1f, 1f })
                {
                    var emitter = new GameObject(side < 0 ? "Emitter L" : "Emitter R");
                    emitter.transform.SetParent(root.transform, false);
                    emitter.transform.localPosition = new Vector3(postX * side, 0f, 0f);
                    Prim(PrimitiveType.Cube, emitter.transform, "Base", new Vector3(0f, 0.08f, 0f), Vector3.zero, new Vector3(0.6f, 0.16f, 0.6f), m.DarkMetal);
                    var post = Prim(PrimitiveType.Cylinder, emitter.transform, "Post", new Vector3(0f, 0.45f, 0f), Vector3.zero, new Vector3(0.28f, 0.3f, 0.28f), m.DarkMetal, keepCollider: true);
                    post.GetComponent<Collider>().isTrigger = false;
                    Prim(PrimitiveType.Cylinder, emitter.transform, "Hazard Band", new Vector3(0f, 0.32f, 0f), Vector3.zero, new Vector3(0.3f, 0.05f, 0.3f), m.Hazard);
                    Prim(PrimitiveType.Cube, emitter.transform, "Head", new Vector3(0f, beamHeight, 0f), Vector3.zero, new Vector3(0.34f, 0.3f, 0.34f), m.Steel);
                    Prim(PrimitiveType.Sphere, emitter.transform, "Lens", new Vector3(-0.17f * side, beamHeight, 0f), Vector3.zero, Vector3.one * 0.2f, m.LaserMid);
                }

                float beamLength = (postX - 0.17f) * 2f;
                var beam = new GameObject("Beam");
                beam.transform.SetParent(root.transform, false);
                beam.transform.localPosition = new Vector3(0f, beamHeight, 0f);
                Prim(PrimitiveType.Cylinder, beam.transform, "Core", Vector3.zero, new Vector3(0f, 0f, 90f), new Vector3(0.06f, beamLength * 0.5f, 0.06f), m.LaserCore);
                Prim(PrimitiveType.Cylinder, beam.transform, "Beam", Vector3.zero, new Vector3(0f, 0f, 90f), new Vector3(0.2f, beamLength * 0.5f, 0.2f), m.LaserMid);
                Prim(PrimitiveType.Cylinder, beam.transform, "Glow", Vector3.zero, new Vector3(0f, 0f, 90f), new Vector3(0.6f, beamLength * 0.5f, 0.6f), m.LaserGlow);
                var trigger = beam.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.size = new Vector3(beamLength, 0.32f, 0.32f);
                beam.AddComponent<LaserHazard>();

                report.AppendLine($"Laser {i + 1}: across '{level1[i].name}' at {F(root.transform.position)}, {beamLength:0.0} m long, {beamHeight} m above the spring.");
            }
            if (level1.Count == 0) report.AppendLine("Lasers: no Level 1 springs found.");
        }

        // ================================================================ Level 2 spinning beams

        private static void BuildSpinners(GameObject[] roots, Transform parent, Mats m, StringBuilder report)
        {
            Vector3 start = PlayerStart();
            Vector3? cp0 = CheckpointPosition(0), cp1 = CheckpointPosition(1);
            if (cp0 == null || cp1 == null) { report.AppendLine("Spinners: checkpoints 0/1 not found — skipped."); return; }

            float startToCp0 = FlatDist(start, cp0.Value);
            float cp0ToCp1 = FlatDist(cp0.Value, cp1.Value);
            var level2 = roots.Where(r => r.activeInHierarchy && r.name.StartsWith("spring_pad")
                                          && FlatDist(start, r.transform.position) > startToCp0
                                          && FlatDist(cp0.Value, r.transform.position) < cp0ToCp1)
                              .OrderBy(r => FlatDist(cp0.Value, r.transform.position)).ToList();

            var poles = roots.Where(r => r.activeInHierarchy && r.name.StartsWith("Cylinder")
                                         && Vector3.Dot(r.transform.up, Vector3.up) > 0.95f).ToList();

            var group = new GameObject("Level 2 Spinning Beams");
            group.transform.SetParent(parent, false);

            // Each pole gets its own personality: (arms, degrees per second, starting angle).
            var configs = new[] { (1, 85f, 0f), (2, -100f, 60f), (1, 130f, 150f) };
            int built = 0;
            for (int i = 0; i < level2.Count; i++)
            {
                Bounds pad = Encapsulate(new[] { level2[i] });
                var pole = poles.FirstOrDefault(p =>
                {
                    Bounds b = Encapsulate(new[] { p });
                    return FlatDist(b.center, pad.center) < pad.extents.x * 0.5f && Mathf.Abs(b.min.y - pad.max.y) < 1.5f;
                });
                if (pole == null) { report.AppendLine($"Spinner: no brown pole found on '{level2[i].name}'."); continue; }

                Bounds pb = Encapsulate(new[] { pole });
                var (arms, speed, startAngle) = configs[built % configs.Length];
                float padRadius = Mathf.Min(pad.extents.x, pad.extents.z);
                float poleRadius = Mathf.Max(pb.extents.x, pb.extents.z);
                float height = pad.max.y + 0.8f;

                var pivot = new GameObject($"Spinning Beam {built + 1}");
                pivot.transform.SetParent(group.transform, false);
                pivot.transform.SetPositionAndRotation(new Vector3(pb.center.x, height, pb.center.z), Quaternion.Euler(0f, startAngle, 0f));

                // Hub: steel collar clamped round the pole, with flanges and bolts.
                float hubR = poleRadius + 0.22f;
                Prim(PrimitiveType.Cylinder, pivot.transform, "Hub Collar", Vector3.zero, Vector3.zero, new Vector3(hubR * 2f, 0.35f, hubR * 2f), m.Steel);
                Prim(PrimitiveType.Cylinder, pivot.transform, "Hub Flange Top", new Vector3(0f, 0.36f, 0f), Vector3.zero, new Vector3(hubR * 2f + 0.22f, 0.04f, hubR * 2f + 0.22f), m.SteelLight);
                Prim(PrimitiveType.Cylinder, pivot.transform, "Hub Flange Bottom", new Vector3(0f, -0.36f, 0f), Vector3.zero, new Vector3(hubR * 2f + 0.22f, 0.04f, hubR * 2f + 0.22f), m.SteelLight);
                for (int b = 0; b < 8; b++)
                {
                    float a = b * 45f * Mathf.Deg2Rad;
                    var bolt = Prim(PrimitiveType.Cylinder, pivot.transform, $"Bolt {b + 1}",
                                    new Vector3(Mathf.Cos(a) * (hubR + 0.02f), 0.2f * (b % 2 == 0 ? 1f : -1f), Mathf.Sin(a) * (hubR + 0.02f)),
                                    Vector3.zero, new Vector3(0.09f, 0.04f, 0.09f), m.SteelLight);
                    bolt.transform.localRotation = Quaternion.LookRotation(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a))) * Quaternion.Euler(90f, 0f, 0f);
                }

                float armStart = hubR + 0.45f;
                float armEnd = padRadius + 0.4f;
                float armLength = armEnd - armStart;
                for (int a = 0; a < arms; a++)
                {
                    var arm = new GameObject($"Arm {a + 1}");
                    arm.transform.SetParent(pivot.transform, false);
                    arm.transform.localRotation = Quaternion.Euler(0f, a * 360f / arms, 0f);

                    // Steel bracket bolting the arm to the hub.
                    Prim(PrimitiveType.Cube, arm.transform, "Bracket", new Vector3(hubR + 0.22f, 0f, 0f), Vector3.zero, new Vector3(0.5f, 0.28f, 0.34f), m.Steel);
                    Prim(PrimitiveType.Cube, arm.transform, "Bracket Plate", new Vector3(hubR + 0.44f, 0f, 0f), Vector3.zero, new Vector3(0.06f, 0.6f, 0.6f), m.SteelLight);

                    // Padded foam sweeper with white stripes and a soft end cap.
                    float mid = armStart + armLength * 0.5f;
                    Prim(PrimitiveType.Cylinder, arm.transform, "Padding", new Vector3(mid, 0f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.62f, armLength * 0.5f, 0.62f), m.Padding);
                    for (float s = armStart + 0.6f; s < armEnd - 0.3f; s += 0.75f)
                        Prim(PrimitiveType.Cylinder, arm.transform, "Stripe", new Vector3(s, 0f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.64f, 0.06f, 0.64f), m.PaddingStripe);
                    Prim(PrimitiveType.Sphere, arm.transform, "End Cap", new Vector3(armEnd, 0f, 0f), Vector3.zero, new Vector3(0.66f, 0.66f, 0.66f), m.EndCap);

                    var hit = arm.AddComponent<CapsuleCollider>();
                    hit.isTrigger = true;
                    hit.direction = 0;   // along X
                    hit.center = new Vector3(mid + 0.15f, 0f, 0f);
                    hit.radius = 0.42f;
                    hit.height = armLength + 1.1f;
                }

                var spinner = pivot.AddComponent<SpinningBeam>();
                spinner.spinSpeed = speed;
                spinner.spinAxis = Vector3.up;
                spinner.knockbackForce = 14f;
                spinner.knockbackUpForce = 6.5f;
                spinner.hitCooldown = 0.6f;

                report.AppendLine($"Spinning Beam {built + 1}: on '{pole.name}' at {F(pivot.transform.position)}, {arms} arm(s), {speed}°/s, reach {armEnd:0.0} m.");
                built++;
            }
            if (built == 0) report.AppendLine("Spinners: none built.");
        }

        // ================================================================ Ocean

        private static void BuildOcean(Transform parent, Bounds course, Mats m, StringBuilder report)
        {
            Mesh mesh = OceanMesh(1500f, 240);
            SaveMesh(mesh, "OceanMesh");

            var ocean = new GameObject("Ocean (animated)");
            ocean.transform.SetParent(parent, false);
            ocean.transform.position = new Vector3(course.center.x, WaterLevel, course.center.z);
            ocean.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = ocean.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m.Water;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;

            var oc = ocean.AddComponent<ObbyKitOcean>();
            var so = new SerializedObject(oc);
            so.FindProperty("splashMaterial").objectReferenceValue = m.Splash;
            so.ApplyModifiedPropertiesWithoutUndo();
            report.AppendLine($"Ocean: 3 km × 3 km animated water at y = {WaterLevel}.");
        }

        /// <summary>Square grid that's dense in the middle (≈3 m) and coarse at the edges (≈20 m).</summary>
        private static Mesh OceanMesh(float half, int n)
        {
            int row = n + 1;
            var verts = new Vector3[row * row];
            var normals = new Vector3[verts.Length];
            float Warp(float u) => Mathf.Sign(u) * (0.25f * Mathf.Abs(u) + 0.75f * u * u);
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    float u = i / (float)n * 2f - 1f, w = j / (float)n * 2f - 1f;
                    verts[j * row + i] = new Vector3(Warp(u) * half, 0f, Warp(w) * half);
                    normals[j * row + i] = Vector3.up;
                }
            var tris = new int[n * n * 6];
            int t = 0;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int a = j * row + i, b = a + 1, c = a + row, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            var mesh = new Mesh { name = "OceanMesh", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(half * 2f, 6f, half * 2f));
            return mesh;
        }

        private static void BuildFoamRings(GameObject[] roots, Transform parent, Mats m, StringBuilder report)
        {
            var group = new GameObject("Foam Rings");
            group.transform.SetParent(parent, false);
            int count = 0;
            foreach (var r in roots.Where(r => r.activeInHierarchy))
            {
                bool pillar = r.name.StartsWith("platform_6x6x4");
                bool spring = r.name.StartsWith("spring_pad");
                bool post = r.name.StartsWith("Cylinder");
                if (!pillar && !spring && !post) continue;
                Bounds b = Encapsulate(new[] { r });
                if (b.size == Vector3.zero || b.min.y > WaterLevel + 0.3f || b.max.y < WaterLevel) continue;

                const float width = 2.2f;
                Vector2 foot;
                Material mat;
                if (pillar) { foot = new Vector2(b.size.x, b.size.z); mat = m.FoamBox; }
                else if (spring) { float d = Mathf.Min(b.size.x, b.size.z) * 0.62f; foot = new Vector2(d, d); mat = m.FoamRound; }
                else { float d = Mathf.Max(b.size.x, b.size.z); foot = new Vector2(d, d); mat = m.FoamRound; }

                var quad = Prim(PrimitiveType.Quad, group.transform, "Foam " + r.name, Vector3.zero, new Vector3(90f, 0f, 0f),
                                new Vector3(foot.x + width * 2f, foot.y + width * 2f, 1f), mat);
                quad.transform.position = new Vector3(b.center.x, WaterLevel + 0.12f, b.center.z);
                var qr = quad.GetComponent<MeshRenderer>();
                qr.shadowCastingMode = ShadowCastingMode.Off;
                qr.receiveShadows = false;
                count++;
            }
            report.AppendLine($"Foam rings: {count} (around pillars, springs and posts standing in the water).");
        }

        // ================================================================ Islands

        private static void BuildIslands(Transform parent, Bounds course, Mats m, StringBuilder report)
        {
            var rng = new System.Random(20261105);
            var group = new GameObject("Distant Islands");
            group.transform.SetParent(parent, false);
            const int count = 10;
            for (int i = 0; i < count; i++)
            {
                float angle = (i / (float)count) * Mathf.PI * 2f + (float)rng.NextDouble() * 0.4f;
                float dist = Mathf.Lerp(430f, 820f, (float)rng.NextDouble());
                float radius = Mathf.Lerp(45f, 110f, (float)rng.NextDouble());
                float height = Mathf.Lerp(16f, 48f, (float)rng.NextDouble());

                Mesh mesh = IslandMesh(rng, radius, height, out List<Vector3> treeSpots);
                mesh.name = $"Island_{i + 1}";
                SaveMesh(mesh, mesh.name);

                var island = new GameObject($"Island {i + 1}");
                island.transform.SetParent(group.transform, false);
                island.transform.position = new Vector3(course.center.x + Mathf.Cos(angle) * dist, WaterLevel, course.center.z + Mathf.Sin(angle) * dist);
                island.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                island.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = island.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { m.Sand, m.Grass, m.Rock };

                int trees = Mathf.Min(treeSpots.Count, 4 + rng.Next(6));
                for (int t = 0; t < trees; t++)
                {
                    Vector3 spot = treeSpots[rng.Next(treeSpots.Count)];
                    float s = Mathf.Lerp(2.4f, 4f, (float)rng.NextDouble());
                    var tree = new GameObject("Tree");
                    tree.transform.SetParent(island.transform, false);
                    tree.transform.localPosition = spot;
                    Prim(PrimitiveType.Cylinder, tree.transform, "Trunk", new Vector3(0f, 1.3f * s, 0f), Vector3.zero, new Vector3(0.7f * s, 1.6f * s, 0.7f * s), m.Trunk);
                    Prim(PrimitiveType.Sphere, tree.transform, "Leaves", new Vector3(0f, 3.4f * s, 0f), Vector3.zero, new Vector3(4.2f * s, 3.4f * s, 4.2f * s), rng.Next(2) == 0 ? m.Leaves : m.LeavesDark);
                    Prim(PrimitiveType.Sphere, tree.transform, "Leaves Top", new Vector3(0.6f * s, 4.6f * s, 0.3f * s), Vector3.zero, new Vector3(2.8f * s, 2.4f * s, 2.8f * s), m.Leaves);
                }
                foreach (Transform t in island.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            }
            report.AppendLine($"Islands: {count} low-poly tropical islands 430–820 m out.");
        }

        /// <summary>Faceted height-field island: sandy beach ring, grassy hills, rocky steep bits.</summary>
        private static Mesh IslandMesh(System.Random rng, float radius, float height, out List<Vector3> treeSpots)
        {
            const int rings = 8, segs = 26;
            float ph1 = (float)rng.NextDouble() * 6.28f, ph2 = (float)rng.NextDouble() * 6.28f, ph3 = (float)rng.NextDouble() * 6.28f;
            var grid = new Vector3[rings + 1, segs];
            treeSpots = new List<Vector3>();
            Vector3 top = new Vector3(0f, height + 1.2f, 0f);

            for (int r = 1; r <= rings; r++)
                for (int s = 0; s < segs; s++)
                {
                    float a = s / (float)segs * Mathf.PI * 2f;
                    float shape = 0.78f + 0.16f * Mathf.Sin(a * 2f + ph1) + 0.08f * Mathf.Sin(a * 3f + ph2) + 0.05f * Mathf.Sin(a * 5f + ph3);
                    float rr = radius * r / rings * shape * (0.94f + 0.12f * (float)rng.NextDouble());
                    float h;
                    if (r == rings) h = -5f;                                                   // under water
                    else if (r == rings - 1) h = 0.5f + (float)rng.NextDouble() * 0.6f;        // beach
                    else
                    {
                        float t = r / (float)(rings - 1);
                        h = height * (1f - Mathf.Pow(t, 1.7f)) * (0.85f + 0.3f * (float)rng.NextDouble()) + 1.2f;
                    }
                    grid[r, s] = new Vector3(Mathf.Cos(a) * rr, h, Mathf.Sin(a) * rr);
                    if (r >= 2 && r <= rings - 3) treeSpots.Add(grid[r, s] - Vector3.up * 0.4f);
                }

            var sand = new List<int>();
            var grass = new List<int>();
            var rock = new List<int>();
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();

            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.y < 0f) { (b, c) = (c, b); n = -n; }
                n.Normalize();
                float avg = (a.y + b.y + c.y) / 3f;
                var list = avg < 2.4f ? sand : (n.y < 0.6f && avg > 6f ? rock : grass);
                int i0 = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                norms.Add(n); norms.Add(n); norms.Add(n);
                list.Add(i0); list.Add(i0 + 1); list.Add(i0 + 2);
            }

            for (int s = 0; s < segs; s++)
            {
                int s2 = (s + 1) % segs;
                Tri(top, grid[1, s], grid[1, s2]);
                for (int r = 1; r < rings; r++)
                {
                    Tri(grid[r, s], grid[r + 1, s], grid[r + 1, s2]);
                    Tri(grid[r, s], grid[r + 1, s2], grid[r, s2]);
                }
            }

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.subMeshCount = 3;
            mesh.SetTriangles(sand, 0);
            mesh.SetTriangles(grass, 1);
            mesh.SetTriangles(rock, 2);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ================================================================ Clouds

        private static void BuildClouds(Transform parent, Bounds course, Mats m, StringBuilder report)
        {
            var rng = new System.Random(7713);
            var sphere = GetPrimitiveMesh(PrimitiveType.Sphere);
            var variants = new Mesh[3];
            for (int v = 0; v < variants.Length; v++)
            {
                var parts = new List<CombineInstance>();
                int puffs = 6 + rng.Next(4);
                for (int p = 0; p < puffs; p++)
                {
                    float x = p == 0 ? 0f : Mathf.Lerp(-1.7f, 1.7f, (float)rng.NextDouble());
                    float y = p == 0 ? 0f : Mathf.Lerp(-0.15f, 0.45f, (float)rng.NextDouble());
                    float z = p == 0 ? 0f : Mathf.Lerp(-0.5f, 0.5f, (float)rng.NextDouble());
                    float s = p == 0 ? 1.2f : Mathf.Lerp(0.55f, 1.0f, (float)rng.NextDouble()) * (1.2f - Mathf.Abs(x) * 0.25f);
                    parts.Add(new CombineInstance { mesh = sphere, transform = Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.identity, new Vector3(s, s * 0.8f, s)) });
                }
                var mesh = new Mesh { name = $"Cloud_{v + 1}" };
                mesh.CombineMeshes(parts.ToArray(), true, true);
                SaveMesh(mesh, mesh.name);
                variants[v] = mesh;
            }

            var group = new GameObject("Drifting Clouds");
            group.transform.SetParent(parent, false);
            group.transform.position = new Vector3(course.center.x, 0f, course.center.z);
            const int count = 26;
            for (int i = 0; i < count; i++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float dist = Mathf.Lerp(260f, 950f, (float)rng.NextDouble());
                float scale = Mathf.Lerp(16f, 34f, (float)rng.NextDouble());
                var cloud = new GameObject($"Cloud {i + 1}");
                cloud.transform.SetParent(group.transform, false);
                cloud.transform.localPosition = new Vector3(Mathf.Cos(angle) * dist, Mathf.Lerp(70f, 190f, (float)rng.NextDouble()), Mathf.Sin(angle) * dist);
                cloud.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                cloud.transform.localScale = Vector3.one * scale;
                cloud.AddComponent<MeshFilter>().sharedMesh = variants[rng.Next(variants.Length)];
                var mr = cloud.AddComponent<MeshRenderer>();
                mr.sharedMaterial = m.Cloud;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            var spin = group.AddComponent<SlowSpin>();
            var so = new SerializedObject(spin);
            so.FindProperty("degreesPerSecond").floatValue = 0.35f;
            so.ApplyModifiedPropertiesWithoutUndo();
            report.AppendLine($"Clouds: {count} puffy clouds drifting slowly around the course.");
        }

        // ================================================================ Sky, light, fog, post

        private static void SetupSkyAndLighting(Mats m, StringBuilder report)
        {
            RenderSettings.skybox = m.Sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.80f, 1.0f);
            RenderSettings.ambientEquatorColor = new Color(0.72f, 0.84f, 0.90f);
            RenderSettings.ambientGroundColor = new Color(0.40f, 0.56f, 0.60f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Horizon;
            RenderSettings.fogStartDistance = 170f;
            RenderSettings.fogEndDistance = 1150f;

            var sun = Object.FindObjectsOfType<Light>().FirstOrDefault(l => l.type == LightType.Directional && l.isActiveAndEnabled);
            if (sun != null)
            {
                Undo.RecordObject(sun, "Sun");
                Undo.RecordObject(sun.transform, "Sun");
                sun.color = new Color(1.0f, 0.95f, 0.86f);
                sun.intensity = 2.2f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.75f;
                sun.transform.rotation = Quaternion.Euler(47f, 330f, 0f);
                RenderSettings.sun = sun;
                report.AppendLine("Sun: warm white, intensity 2.2, soft shadows.");
            }
            DynamicGI.UpdateEnvironment();

            var tpc = Object.FindObjectOfType<ThirdPersonCamera>();
            Camera cam = tpc != null ? tpc.GetComponent<Camera>() : Camera.main;
            if (cam != null)
            {
                Undo.RecordObject(cam, "Camera far plane");
                cam.farClipPlane = 2200f;
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    Undo.RecordObject(data, "Camera post-processing");
                    data.renderPostProcessing = true;
                }
            }
            report.AppendLine("Sky: cartoon gradient with sun and clouds. Ambient: sky/horizon/ground colours. Fog: soft horizon haze 170–1150 m.");
        }

        private static void SetupPostProcessing(GameObject[] roots, StringBuilder report)
        {
            var volume = Object.FindObjectsOfType<Volume>().FirstOrDefault(v => v.isGlobal);
            if (volume == null)
            {
                var go = new GameObject("Global Volume");
                volume = go.AddComponent<Volume>();
                volume.isGlobal = true;
                Undo.RegisterCreatedObjectUndo(go, "Global Volume");
            }

            string path = EnvDir + "/ObbyKit_CartoonPost.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.7f);
            bloom.scatter.Override(0.65f);
            var color = profile.Add<ColorAdjustments>(true);
            color.saturation.Override(18f);
            color.contrast.Override(8f);
            color.postExposure.Override(0.15f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.5f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);

            Undo.RecordObject(volume, "Post-processing profile");
            volume.sharedProfile = profile;
            report.AppendLine("Post-processing: neutral tonemapping, bloom (makes the lasers glow), +18 saturation, soft vignette.");
        }

        private static void AdjustVoidZone(StringBuilder report)
        {
            var vz = Object.FindObjectOfType<VoidZone>();
            if (vz == null) return;
            var box = vz.GetComponent<BoxCollider>();
            if (box == null) return;
            float top = WaterLevel - 0.7f;
            Undo.RecordObject(vz.transform, "Void height");
            float halfHeight = box.size.y * 0.5f * vz.transform.lossyScale.y;
            Vector3 p = vz.transform.position;
            p.y = top - halfHeight - box.center.y * vz.transform.lossyScale.y;
            vz.transform.position = p;
            report.AppendLine($"VoidZone moved: respawn now triggers at y = {top:0.0}, just under the water surface.");
        }

        // ================================================================ Materials

        private static Mats MakeMaterials(StringBuilder report)
        {
            Shader water = Shader.Find("ObbyKit/CartoonWater"), sky = Shader.Find("ObbyKit/CartoonSky"),
                   laser = Shader.Find("ObbyKit/Laser"), foam = Shader.Find("ObbyKit/FoamRing"), splash = Shader.Find("ObbyKit/SplashParticle");
            if (water == null || sky == null || laser == null || foam == null || splash == null)
            {
                Debug.LogError("[ObbyKit Environment] ObbyKit shaders not found. Check Assets/ObbyKit/Shaders compiled without errors.");
                return null;
            }

            var m = new Mats
            {
                LaserCore = Custom("Laser_Core", laser, mat => { mat.SetColor("_Color", new Color(3.2f, 1.4f, 1.5f)); mat.SetFloat("_CoreSharpness", 0.4f); }),
                LaserMid = Custom("Laser_Mid", laser, mat => { mat.SetColor("_Color", new Color(2.6f, 0.06f, 0.14f)); mat.SetFloat("_CoreSharpness", 0.5f); }),
                LaserGlow = Custom("Laser_Glow", laser, mat => { mat.SetColor("_Color", new Color(1.1f, 0.02f, 0.07f)); mat.SetFloat("_CoreSharpness", 1.4f); }),
                DarkMetal = Lit("Metal_Dark", Hex("2B2F36"), 0.45f, 0.7f),
                Steel = Lit("Metal_Steel", Hex("8C96A3"), 0.7f, 0.9f),
                SteelLight = Lit("Metal_SteelLight", Hex("C5CDD6"), 0.75f, 0.9f),
                Hazard = Lit("Hazard_Yellow", Hex("FFC21A"), 0.4f, 0f),
                Padding = Lit("Sweeper_Padding", Hex("E8343B"), 0.35f, 0f),
                PaddingStripe = Lit("Sweeper_Stripe", Hex("F7F7F7"), 0.35f, 0f),
                EndCap = Lit("Sweeper_EndCap", Hex("FFC21A"), 0.4f, 0f),
                Water = Custom("Ocean_Water", water, mat =>
                {
                    mat.SetFloat("_FoamAmount", 0.4f);
                    mat.SetFloat("_FoamLineWidth", 0.35f);
                    mat.SetFloat("_FoamScale", 0.07f);
                }),
                FoamBox = Custom("Foam_Box", foam, mat => mat.SetFloat("_Roundness", 0.12f)),
                FoamRound = Custom("Foam_Round", foam, mat => mat.SetFloat("_Roundness", 1f)),
                Splash = Custom("Splash_Droplet", splash, mat => { }),
                Sky = Custom("Sky_Cartoon", sky, mat => { mat.SetColor("_HorizonColor", Horizon); }),
                Sand = Lit("Island_Sand", Hex("F2D99A"), 0.1f, 0f),
                Grass = Lit("Island_Grass", Hex("5CC85A"), 0.1f, 0f),
                Rock = Lit("Island_Rock", Hex("8F8A84"), 0.15f, 0f),
                Trunk = Lit("Tree_Trunk", Hex("8A5A34"), 0.1f, 0f),
                Leaves = Lit("Tree_Leaves", Hex("3FB34F"), 0.15f, 0f),
                LeavesDark = Lit("Tree_LeavesDark", Hex("2E9444"), 0.15f, 0f),
                Cloud = Lit("Cloud_Puffy", Color.white, 0.05f, 0f, new Color(0.30f, 0.33f, 0.38f)),
            };
            report.AppendLine("Materials created in " + MatDir);
            return m;
        }

        private static Material Lit(string name, Color color, float smoothness, float metallic, Color? emission = null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            return Custom(name, shader, mat =>
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
                if (emission.HasValue && mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", emission.Value);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
            });
        }

        private static Material Custom(string name, Shader shader, Action<Material> setup)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }
            setup(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ================================================================ Helpers

        private static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 localPos, Vector3 localEuler,
                                       Vector3 localScale, Material mat, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private static Mesh GetPrimitiveMesh(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            return mesh;
        }

        private static void SaveMesh(Mesh mesh, string name)
        {
            string path = $"{MeshDir}/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
        }

        private static Bounds Encapsulate(IEnumerable<GameObject> objects)
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

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color c);
            return c;
        }

        private static string F(Vector3 v) => $"({v.x:0.0}, {v.y:0.0}, {v.z:0.0})";
    }
}
