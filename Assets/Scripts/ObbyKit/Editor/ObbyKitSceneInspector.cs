// =====================================================================================
//  ObbyKitSceneInspector.cs  —  ObbyKit (Editor only, read-only helpers)
// -------------------------------------------------------------------------------------
//  Tools ▸ ObbyKit ▸ Debug ▸ Dump Scene Info
//      Writes ObbyKitSceneDump.txt (project folder): every top-level object with its
//      position, rotation, scale, layer, components, materials, colliders and bounds.
//  Tools ▸ ObbyKit ▸ Debug ▸ Render Snapshots
//      Reads ObbyKitSnapshots.json (project folder) and renders each requested view to
//      ObbyKitSnap_<name>.png using the scene's own lighting and sky.
//  Neither changes the scene.
// =====================================================================================

using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ObbyKit.EditorTools
{
    public static class ObbyKitSceneInspector
    {
        [Serializable] private class View { public string name; public float[] pos; public float[] look; public float fov = 50f; public int width = 960; public int height = 540; }
        [Serializable] private class ViewList { public View[] views; }

        private static string ProjectDir => Path.GetDirectoryName(Application.dataPath) ?? ".";

        [MenuItem("Tools/ObbyKit/Debug/Dump Scene Info")]
        public static void Dump()
        {
            Scene scene = SceneManager.GetActiveScene();
            var sb = new StringBuilder();
            sb.AppendLine($"Scene {scene.path}");
            sb.AppendLine($"Skybox: {(RenderSettings.skybox ? RenderSettings.skybox.name + " / " + RenderSettings.skybox.shader.name : "none")}, ambient mode {RenderSettings.ambientMode}, fog {RenderSettings.fog}");
            foreach (var root in scene.GetRootGameObjects())
            {
                Describe(sb, root.transform, 0, root.name.StartsWith("barrier") ? 0 : 2);
            }
            File.WriteAllText(Path.Combine(ProjectDir, "ObbyKitSceneDump.txt"), sb.ToString());
            Debug.Log("[ObbyKit Debug] Scene dump written.");
        }

        private static void Describe(StringBuilder sb, Transform t, int depth, int maxDepth)
        {
            var go = t.gameObject;
            string indent = new string(' ', depth * 2);
            var comps = go.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name);
            sb.Append(indent).Append(go.name).Append(go.activeInHierarchy ? "" : " [inactive]")
              .Append($" pos{F(t.position)} rot{F(t.eulerAngles)} scale{F(t.lossyScale)} layer={LayerMask.LayerToName(go.layer)}")
              .Append(" {").Append(string.Join(",", comps)).Append('}');

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                sb.Append($" bounds min{F(b.min)} max{F(b.max)}");
                var mats = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct().Take(4);
                sb.Append(" mats[").Append(string.Join(",", mats)).Append(']');
            }
            foreach (var c in go.GetComponents<Collider>())
                sb.Append($" col:{c.GetType().Name}{(c.isTrigger ? "(trigger)" : "")}{F(c.bounds.size)}");
            var sbm = go.GetComponent("SpinningBeam");
            if (sbm != null) sb.Append(" SPINNING_BEAM ").Append(EditorJsonUtility.ToJson(sbm));
            sb.AppendLine();

            if (depth < maxDepth)
                foreach (Transform child in t) Describe(sb, child, depth + 1, maxDepth);
        }

        [MenuItem("Tools/ObbyKit/Debug/Render Snapshots")]
        public static void Snapshots()
        {
            string jsonPath = Path.Combine(ProjectDir, "ObbyKitSnapshots.json");
            if (!File.Exists(jsonPath)) { Debug.LogWarning("[ObbyKit Debug] No ObbyKitSnapshots.json in the project folder."); return; }
            var list = JsonUtility.FromJson<ViewList>(File.ReadAllText(jsonPath));

            var camGo = new GameObject("ObbyKit Snapshot Camera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 2500f;
            var camData = cam.GetUniversalAdditionalCameraData();
            if (camData != null) camData.renderPostProcessing = true;   // show bloom etc. like the game camera
            try
            {
                foreach (var v in list.views)
                {
                    var rt = new RenderTexture(v.width, v.height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                    cam.targetTexture = rt;
                    cam.fieldOfView = v.fov;
                    Vector3 pos = new Vector3(v.pos[0], v.pos[1], v.pos[2]);
                    Vector3 look = new Vector3(v.look[0], v.look[1], v.look[2]);
                    cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(v.width, v.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, v.width, v.height), 0, 0);
                    tex.Apply();
                    File.WriteAllBytes(Path.Combine(ProjectDir, $"ObbyKitSnap_{v.name}.png"), tex.EncodeToPNG());
                    RenderTexture.active = null;
                    cam.targetTexture = null;
                    UnityEngine.Object.DestroyImmediate(tex);
                    UnityEngine.Object.DestroyImmediate(rt);
                }
            }
            finally
            {
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(camGo);
            }
            Debug.Log($"[ObbyKit Debug] Rendered {list.views.Length} snapshots.");
        }

        private static string F(Vector3 v) => $"({v.x:0.##},{v.y:0.##},{v.z:0.##})";
    }
}
