// =====================================================================================
//  ObbyKitCharacterBuilder.cs  —  ObbyKit (Editor only)
// -------------------------------------------------------------------------------------
//  Tools ▸ ObbyKit ▸ Create New Player Character
//
//  Builds an original blocky third-person character ("Obby Runner") entirely from code:
//   • Model: a jointed, blocky humanoid (Hips ▸ Torso ▸ Head/Arms, Hips ▸ Legs) made
//     from cubes with URP materials. Feet at y = 0, about 1.95 m tall.
//   • Animations (generic clips with transform curves, saved as .anim assets):
//       Idle, Walk, Run, Sprint, Jump, Fall, WallRunLeft, WallRunRight,
//       HangIdle, HangForward (also played backwards for hanging backwards).
//   • Animator Controller "ObbyRunner" with the parameters PlayerAnimationController drives:
//       Speed (float m/s), IsGrounded, IsJumping, VerticalVelocity, IsWallRunning,
//       WallSide (-1 left / +1 right), IsHanging, HangSpeed (-1..1), Respawn (trigger).
//   • Prefab "ObbyPlayer": CharacterController + PlayerController (settings copied from
//     your current player) + PlayerAnimationController + the model with its Animator.
//   • Scene: places ObbyPlayer where your current player stands, DEACTIVATES the old
//     player (it isn't deleted), and adds a "Third Person Camera" (a copy of your current
//     camera's settings) with ThirdPersonCamera.
//   • Writes ObbyKitCharacterPreview.png (each animation drawn at several moments) and
//     ObbyKitCharacterReport.txt in the project folder.
//
//  Assets go in Assets/ObbyKit/Character/. Running it again rebuilds everything.
// =====================================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ObbyKit.EditorTools
{
    public static class ObbyKitCharacterBuilder
    {
        private const string RootDir = "Assets/ObbyKit/Character";
        private const string MatDir = RootDir + "/Materials";
        private const string AnimDir = RootDir + "/Animations";
        private const string PlayerName = "ObbyPlayer";
        private const string CameraName = "Third Person Camera";
        private const float HipHeight = 0.95f;
        private const float TwoPi = Mathf.PI * 2f;

        // Joint name -> path from the Model (Animator) root. Order matters for the curves.
        private static readonly string[] Joints =
            { "Hips", "Torso", "Head", "ShoulderL", "ElbowL", "ShoulderR", "ElbowR",
              "HipL", "KneeL", "AnkleL", "HipR", "KneeR", "AnkleR" };

        private static readonly Dictionary<string, string> JointPaths = new Dictionary<string, string>
        {
            { "Hips", "Hips" },
            { "Torso", "Hips/Torso" },
            { "Head", "Hips/Torso/Head" },
            { "ShoulderL", "Hips/Torso/ShoulderL" },
            { "ElbowL", "Hips/Torso/ShoulderL/ElbowL" },
            { "ShoulderR", "Hips/Torso/ShoulderR" },
            { "ElbowR", "Hips/Torso/ShoulderR/ElbowR" },
            { "HipL", "Hips/HipL" },
            { "KneeL", "Hips/HipL/KneeL" },
            { "AnkleL", "Hips/HipL/KneeL/AnkleL" },
            { "HipR", "Hips/HipR" },
            { "KneeR", "Hips/HipR/KneeR" },
            { "AnkleR", "Hips/HipR/KneeR/AnkleR" },
        };

        private class Mats { public Material Shirt, Accent, Pants, Skin, Hair, Shoe, Dark, Eye; }

        // A clip is a set of functions of phase p (0..2π over the clip length), in degrees / metres.
        private class Pose : Dictionary<string, Func<float, float>> { }

        private static float S(float p) => Mathf.Sin(p);
        private static float C(float p) => Mathf.Cos(p);

        [MenuItem("Tools/ObbyKit/Create New Player Character")]
        public static void Build()
        {
            var report = new StringBuilder();
            Scene scene = SceneManager.GetActiveScene();
            report.AppendLine($"ObbyKit character builder — scene '{scene.path}'");

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create ObbyKit Player Character");

            EnsureFolder("Assets", "ObbyKit");
            EnsureFolder("Assets/ObbyKit", "Character");
            EnsureFolder(RootDir, "Materials");
            EnsureFolder(RootDir, "Animations");

            // ---------------------------------------------------------------- Remove previous build from the scene
            foreach (var go in scene.GetRootGameObjects().Where(g => g.name == PlayerName || g.name == CameraName).ToList())
                Undo.DestroyObjectImmediate(go);

            // ---------------------------------------------------------------- Template = your current player
            var template = UnityEngine.Object.FindObjectsOfType<PlayerController>(true)
                .Where(pc => pc.gameObject.scene == scene)
                .OrderByDescending(pc => pc.GetComponent<PlayerMovement>() != null)
                .ThenByDescending(pc => pc.gameObject.activeInHierarchy)
                .FirstOrDefault();
            if (template == null)
            {
                Debug.LogError("[ObbyKit Character] No PlayerController found. Run Tools ▸ ObbyKit ▸ Set Up Scene first.");
                return;
            }
            var templateCC = template.GetComponent<CharacterController>();
            float tScale = template.transform.lossyScale.y;
            Vector3 templateFeet = template.transform.TransformPoint(templateCC.center) - Vector3.up * (templateCC.height * 0.5f * tScale);
            float templatePivotToTop = (templateCC.center.y + templateCC.height * 0.5f) * tScale;
            Camera templateCam = template.GetComponentInChildren<Camera>(true);
            report.AppendLine($"Template player '{template.name}', feet at {Fmt(templateFeet)}, camera '{(templateCam ? templateCam.name : "none")}'.");

            // ---------------------------------------------------------------- Materials, model, clips, controller
            Mats mats = MakeMaterials();
            GameObject model = BuildModel(mats);
            Dictionary<string, AnimationClip> clips = MakeClips();
            AnimatorController controller = MakeController(clips);
            report.AppendLine($"Created {clips.Count} animation clips and Animator Controller '{controller.name}'.");

            // ---------------------------------------------------------------- Player object
            var player = new GameObject(PlayerName);
            player.transform.SetPositionAndRotation(templateFeet, Quaternion.Euler(0f, template.transform.eulerAngles.y, 0f));
            int playerLayer = LayerMask.NameToLayer("Player");

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.95f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, cc.height * 0.5f, 0f);
            cc.stepOffset = 0.35f;
            cc.slopeLimit = 50f;
            cc.skinWidth = 0.05f;

            model.transform.SetParent(player.transform, false);
            var animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var pc = player.AddComponent<PlayerController>();
            EditorUtility.CopySerialized(template, pc);   // keeps your speeds, jump, levels, wallrun tuning, layers...
            float newPivotToTop = cc.center.y + cc.height * 0.5f;
            var pso = new SerializedObject(pc);
            pso.FindProperty("controlScheme").enumValueIndex = (int)ControlScheme.CameraRelative;
            pso.FindProperty("cameraTransform").objectReferenceValue = null;     // set on the scene instance below
            pso.FindProperty("cameraTiltPivot").objectReferenceValue = null;     // ThirdPersonCamera reads CameraRoll instead
            var hang = pso.FindProperty("hangOffset");
            hang.floatValue = hang.floatValue - templatePivotToTop + newPivotToTop;
            pso.FindProperty("turnSpeed").floatValue = 900f;
            pso.ApplyModifiedPropertiesWithoutUndo();

            var pac = player.AddComponent<PlayerAnimationController>();
            var aso = new SerializedObject(pac);
            aso.FindProperty("player").objectReferenceValue = pc;
            aso.FindProperty("animator").objectReferenceValue = animator;
            aso.ApplyModifiedPropertiesWithoutUndo();

            player.AddComponent<DoubleJumpIndicator>();   // on-screen "double jump ready" icon

            if (playerLayer >= 0)
                foreach (Transform t in player.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = playerLayer;
            player.tag = "Player";

            string prefabPath = RootDir + "/ObbyPlayer.prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(player, prefabPath, InteractionMode.AutomatedAction);
            Undo.RegisterCreatedObjectUndo(player, "Create ObbyPlayer");
            report.AppendLine($"Player prefab saved to {prefabPath}. hangOffset {hang.floatValue:0.00}.");

            // ---------------------------------------------------------------- Camera
            GameObject camGo;
            if (templateCam != null)
            {
                camGo = UnityEngine.Object.Instantiate(templateCam.gameObject);
                foreach (var col in camGo.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(col);
                foreach (var mb in camGo.GetComponents<MonoBehaviour>())
                    if (mb != null && mb.GetType().Namespace != "UnityEngine.Rendering.Universal" && !(mb is ThirdPersonCamera))
                        UnityEngine.Object.DestroyImmediate(mb);
            }
            else
            {
                camGo = new GameObject();
                camGo.AddComponent<Camera>();
            }
            camGo.name = CameraName;
            camGo.tag = "MainCamera";
            camGo.layer = 0;
            camGo.SetActive(true);
            var listener = camGo.GetComponent<AudioListener>();
            if (listener == null) listener = camGo.AddComponent<AudioListener>();
            listener.enabled = true;
            camGo.transform.SetPositionAndRotation(templateFeet + player.transform.rotation * new Vector3(0.35f, 2.6f, -4.5f),
                                                   player.transform.rotation * Quaternion.Euler(15f, 0f, 0f));
            var tpc = camGo.AddComponent<ThirdPersonCamera>();
            var cso = new SerializedObject(tpc);
            cso.FindProperty("target").objectReferenceValue = player.transform;
            int mask = ~0;
            if (playerLayer >= 0) mask &= ~(1 << playerLayer);
            int barLayer = LayerMask.NameToLayer("MonkeyBar");
            if (barLayer >= 0) mask &= ~(1 << barLayer);
            cso.FindProperty("collisionMask").intValue = mask;
            cso.ApplyModifiedPropertiesWithoutUndo();
            Undo.RegisterCreatedObjectUndo(camGo, "Create Third Person Camera");

            // Point the player (scene instance) at the new camera.
            var ipso = new SerializedObject(pc);
            ipso.FindProperty("cameraTransform").objectReferenceValue = camGo.transform;
            ipso.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(pc);

            // ---------------------------------------------------------------- Retire the old player
            Undo.RecordObject(template.gameObject, "Deactivate old player");
            template.gameObject.SetActive(false);
            if (PrefabUtility.IsPartOfPrefabInstance(template.gameObject))
                PrefabUtility.RecordPrefabInstancePropertyModifications(template.gameObject);
            report.AppendLine($"Old player '{template.name}' deactivated (not deleted). New camera '{CameraName}' added.");

            // ---------------------------------------------------------------- Preview sheet
            string projectDir = Path.GetDirectoryName(Application.dataPath) ?? ".";
            string previewPath = Path.Combine(projectDir, "ObbyKitCharacterPreview.png");
            try
            {
                RenderPreview(player, clips, previewPath, report);
            }
            catch (Exception e)
            {
                report.AppendLine("Preview render failed: " + e.Message);
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = player;

            string reportPath = Path.Combine(projectDir, "ObbyKitCharacterReport.txt");
            File.WriteAllText(reportPath, report.ToString());
            Debug.Log("[ObbyKit Character] Done. Scene saved.\n" + report);
        }

        // ================================================================ Materials

        private static Mats MakeMaterials()
        {
            return new Mats
            {
                Shirt = Mat("Runner_Shirt", Hex("2F80ED")),
                Accent = Mat("Runner_Accent", Hex("FF8A00")),
                Pants = Mat("Runner_Pants", Hex("26324A")),
                Skin = Mat("Runner_Skin", Hex("F2C49B")),
                Hair = Mat("Runner_Hair", Hex("3B2A20")),
                Shoe = Mat("Runner_Shoes", Hex("F4F4F4")),
                Dark = Mat("Runner_Dark", Hex("1C1C22")),
                Eye = Mat("Runner_Eyes", Hex("111111"), 0.7f),
            };
        }

        private static Material Mat(string name, Color color, float smoothness = 0.25f)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            AssetDatabase.CreateAsset(mat, $"{MatDir}/{name}.mat");
            return mat;
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color c);
            return c;
        }

        // ================================================================ Model

        private static GameObject BuildModel(Mats m)
        {
            var model = new GameObject("Model");
            Transform hips = Joint(model.transform, "Hips", new Vector3(0f, HipHeight, 0f));
            Part(hips, "Pelvis", new Vector3(0.42f, 0.2f, 0.25f), new Vector3(0f, 0f, 0f), m.Pants);

            Transform torso = Joint(hips, "Torso", new Vector3(0f, 0.06f, 0f));
            Part(torso, "Chest", new Vector3(0.5f, 0.5f, 0.28f), new Vector3(0f, 0.27f, 0f), m.Shirt);
            Part(torso, "Stripe", new Vector3(0.505f, 0.07f, 0.285f), new Vector3(0f, 0.36f, 0f), m.Accent);
            Part(torso, "Belt", new Vector3(0.44f, 0.06f, 0.27f), new Vector3(0f, 0.03f, 0f), m.Dark);

            Transform head = Joint(torso, "Head", new Vector3(0f, 0.52f, 0f));
            Part(head, "Neck", new Vector3(0.16f, 0.08f, 0.16f), new Vector3(0f, 0.02f, 0f), m.Skin);
            Part(head, "Skull", new Vector3(0.34f, 0.32f, 0.32f), new Vector3(0f, 0.2f, 0f), m.Skin);
            Part(head, "Hair", new Vector3(0.36f, 0.09f, 0.34f), new Vector3(0f, 0.37f, -0.01f), m.Hair);
            Part(head, "HairBack", new Vector3(0.36f, 0.2f, 0.08f), new Vector3(0f, 0.28f, -0.14f), m.Hair);
            Part(head, "EyeL", new Vector3(0.05f, 0.08f, 0.02f), new Vector3(-0.075f, 0.22f, 0.161f), m.Eye);
            Part(head, "EyeR", new Vector3(0.05f, 0.08f, 0.02f), new Vector3(0.075f, 0.22f, 0.161f), m.Eye);

            foreach (var (side, sign) in new[] { ("L", -1f), ("R", 1f) })
            {
                Transform shoulder = Joint(torso, "Shoulder" + side, new Vector3(0.32f * sign, 0.46f, 0f));
                Part(shoulder, "UpperArm" + side, new Vector3(0.15f, 0.3f, 0.15f), new Vector3(0f, -0.13f, 0f), m.Shirt);
                Transform elbow = Joint(shoulder, "Elbow" + side, new Vector3(0f, -0.28f, 0f));
                Part(elbow, "Forearm" + side, new Vector3(0.13f, 0.24f, 0.13f), new Vector3(0f, -0.12f, 0f), m.Skin);
                Part(elbow, "Hand" + side, new Vector3(0.14f, 0.12f, 0.14f), new Vector3(0f, -0.29f, 0f), m.Skin);

                Transform hip = Joint(hips, "Hip" + side, new Vector3(0.11f * sign, -0.04f, 0f));
                Part(hip, "Thigh" + side, new Vector3(0.18f, 0.44f, 0.2f), new Vector3(0f, -0.21f, 0f), m.Pants);
                Transform knee = Joint(hip, "Knee" + side, new Vector3(0f, -0.43f, 0f));
                Part(knee, "Shin" + side, new Vector3(0.16f, 0.4f, 0.18f), new Vector3(0f, -0.2f, 0f), m.Pants);
                Transform ankle = Joint(knee, "Ankle" + side, new Vector3(0f, -0.41f, 0f));
                Part(ankle, "Shoe" + side, new Vector3(0.19f, 0.1f, 0.3f), new Vector3(0f, -0.04f, 0.05f), m.Shoe);
            }
            return model;
        }

        private static Transform Joint(Transform parent, string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        private static void Part(Transform parent, string name, Vector3 size, Vector3 localPos, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ================================================================ Animation clips
        //  Joint conventions (character faces +Z, limbs hang along -Y):
        //   • Hip/Shoulder X: negative = swing forward, positive = swing back.
        //   • Knee X positive = shin folds back. Elbow X negative = forearm folds forward.
        //   • Ankle X positive = toes point down.
        //   • Z on the left limbs: negative = out to the side (right limbs: positive).
        //   • Hips/Torso Z positive = lean to the character's left.

        private static Dictionary<string, AnimationClip> MakeClips()
        {
            var clips = new Dictionary<string, AnimationClip>();

            clips["Idle"] = SaveClip("Idle", 2.4f, IdlePose());
            clips["Walk"] = SaveClip("Walk", 1.0f, Gait(28f, 6f, 40f, 22f, 15f, 3f, 0.025f, 6f));
            clips["Run"] = SaveClip("Run", 0.66f, Gait(42f, 12f, 70f, 45f, 80f, 9f, 0.045f, 10f));
            clips["Sprint"] = SaveClip("Sprint", 0.52f, Gait(55f, 15f, 95f, 65f, 95f, 16f, 0.06f, 14f));
            clips["Jump"] = SaveClip("Jump", 1.2f, JumpPose());
            clips["Fall"] = SaveClip("Fall", 0.8f, FallPose());
            Pose wallRight = WallRunRightPose();
            clips["WallRunRight"] = SaveClip("WallRunRight", 0.55f, wallRight);
            clips["WallRunLeft"] = SaveClip("WallRunLeft", 0.55f, Mirror(wallRight));
            clips["HangIdle"] = SaveClip("HangIdle", 2.6f, HangIdlePose());
            clips["HangForward"] = SaveClip("HangForward", 1.0f, HangForwardPose());
            clips["DoubleJumpFlip"] = SaveClip("DoubleJumpFlip", 0.5f, FlipPose(), loop: false);
            return clips;
        }

        /// <summary>Tucked front flip for the double jump: one full forward rotation around the hips.</summary>
        private static Pose FlipPose()
        {
            // Tuck amount: 0 at the start and end, fully tucked halfway through.
            Func<float, float> tuck = p => Sq(Mathf.Sin(p * 0.5f));
            return new Pose
            {
                ["Hips.py"] = p => HipHeight,
                ["Hips.x"] = p => p * Mathf.Rad2Deg,          // 0 → 360°: positive X pitches the body forward
                ["Torso.x"] = p => 20f * tuck(p),
                ["Head.x"] = p => 25f * tuck(p),
                ["HipL.x"] = p => -100f * tuck(p), ["HipR.x"] = p => -100f * tuck(p),
                ["KneeL.x"] = p => 120f * tuck(p), ["KneeR.x"] = p => 120f * tuck(p),
                ["AnkleL.x"] = p => 30f * tuck(p), ["AnkleR.x"] = p => 30f * tuck(p),
                ["ShoulderL.x"] = p => -70f * tuck(p), ["ShoulderR.x"] = p => -70f * tuck(p),
                ["ShoulderL.z"] = p => -10f * tuck(p), ["ShoulderR.z"] = p => 10f * tuck(p),
                ["ElbowL.x"] = p => -80f * tuck(p), ["ElbowR.x"] = p => -80f * tuck(p),
            };
        }

        private static Pose IdlePose()
        {
            return new Pose
            {
                ["Hips.py"] = p => HipHeight - 0.012f - 0.008f * S(p),
                ["Torso.x"] = p => 1.5f * S(p),
                ["Head.x"] = p => -1f * S(p),
                ["ShoulderL.x"] = p => 3f, ["ShoulderR.x"] = p => 3f,
                ["ShoulderL.z"] = p => -5f - 2f * S(p), ["ShoulderR.z"] = p => 5f + 2f * S(p),
                ["ElbowL.x"] = p => -10f, ["ElbowR.x"] = p => -10f,
                ["HipL.z"] = p => -3f, ["HipR.z"] = p => 3f,
                ["KneeL.x"] = p => 4f, ["KneeR.x"] = p => 4f,
                ["AnkleL.x"] = p => -2f, ["AnkleR.x"] = p => -2f,
                ["AnkleL.z"] = p => 3f, ["AnkleR.z"] = p => -3f,
            };
        }

        /// <summary>Walk / run / sprint cycle. Left leg is forward when sin(p) > 0.</summary>
        private static Pose Gait(float legSwing, float kneeBase, float kneeLift, float armSwing, float elbow,
                                 float lean, float bob, float twist)
        {
            Func<float, float> hipL = p => -legSwing * S(p);
            Func<float, float> hipR = p => legSwing * S(p);
            Func<float, float> kneeL = p => kneeBase + kneeLift * Sq(0.5f + 0.5f * C(p));
            Func<float, float> kneeR = p => kneeBase + kneeLift * Sq(0.5f + 0.5f * C(p + Mathf.PI));
            return new Pose
            {
                ["Hips.py"] = p => HipHeight - bob + bob * Mathf.Abs(C(p)) - kneeBase * 0.0015f,
                ["Hips.y"] = p => 0.5f * twist * S(p),
                ["Torso.x"] = p => lean + 1.5f * S(2f * p),
                ["Torso.y"] = p => -1.3f * twist * S(p),
                ["Head.x"] = p => -lean * 0.6f,
                ["Head.y"] = p => 0.8f * twist * S(p),
                ["HipL.x"] = hipL, ["HipR.x"] = hipR,
                ["KneeL.x"] = kneeL, ["KneeR.x"] = kneeR,
                // Partly level the feet: cancel half of the hip + knee angle.
                ["AnkleL.x"] = p => -0.45f * (hipL(p) + kneeL(p)) + 0.2f * kneeLift * Sq(0.5f + 0.5f * C(p)),
                ["AnkleR.x"] = p => -0.45f * (hipR(p) + kneeR(p)) + 0.2f * kneeLift * Sq(0.5f + 0.5f * C(p + Mathf.PI)),
                ["ShoulderL.x"] = p => armSwing * S(p),
                ["ShoulderR.x"] = p => -armSwing * S(p),
                ["ShoulderL.z"] = p => -6f, ["ShoulderR.z"] = p => 6f,
                ["ElbowL.x"] = p => -elbow + 0.25f * elbow * S(p),
                ["ElbowR.x"] = p => -elbow - 0.25f * elbow * S(p),
            };
        }

        private static Pose JumpPose()
        {
            return new Pose
            {
                ["Hips.py"] = p => HipHeight,
                ["Torso.x"] = p => 6f + 1.5f * S(p),
                ["Head.x"] = p => -8f,
                ["HipL.x"] = p => -65f + 3f * S(p), ["KneeL.x"] = p => 85f + 3f * S(p), ["AnkleL.x"] = p => 10f,
                ["HipR.x"] = p => 12f + 3f * S(p), ["KneeR.x"] = p => 35f, ["AnkleR.x"] = p => 20f,
                ["ShoulderL.x"] = p => -40f + 4f * S(p), ["ShoulderR.x"] = p => -140f + 4f * S(p),
                ["ShoulderL.z"] = p => -25f, ["ShoulderR.z"] = p => 20f,
                ["ElbowL.x"] = p => -40f, ["ElbowR.x"] = p => -25f,
            };
        }

        private static Pose FallPose()
        {
            return new Pose
            {
                ["Hips.py"] = p => HipHeight,
                ["Torso.x"] = p => -6f,
                ["Head.x"] = p => 10f,
                ["ShoulderL.z"] = p => -110f - 15f * S(p), ["ShoulderR.z"] = p => 110f + 15f * S(p),
                ["ShoulderL.x"] = p => -15f + 10f * C(p), ["ShoulderR.x"] = p => -15f - 10f * C(p),
                ["ElbowL.x"] = p => -35f + 10f * S(p), ["ElbowR.x"] = p => -35f - 10f * S(p),
                ["HipL.x"] = p => -30f + 15f * S(p), ["HipR.x"] = p => -5f - 15f * S(p),
                ["KneeL.x"] = p => 50f - 15f * S(p), ["KneeR.x"] = p => 35f + 15f * S(p),
                ["AnkleL.x"] = p => 15f, ["AnkleR.x"] = p => 15f,
            };
        }

        /// <summary>Wall on the character's RIGHT: body leans away (left) so the feet run on the wall.</summary>
        private static Pose WallRunRightPose()
        {
            Pose pose = Gait(45f, 14f, 75f, 35f, 80f, 6f, 0.03f, 8f);
            pose["Hips.z"] = p => 24f;
            pose["Torso.z"] = p => 6f;
            pose["Head.z"] = p => -18f;
            pose["HipL.z"] = p => 8f;
            pose["HipR.z"] = p => 10f;
            pose["ShoulderL.z"] = p => -60f + 6f * S(p);          // outside arm out for balance
            pose["ShoulderL.x"] = p => -15f + 20f * S(p);
            pose["ElbowL.x"] = p => -25f;
            pose["ShoulderR.z"] = p => 25f;                        // wall-side arm reaching toward the wall
            pose["ShoulderR.x"] = p => -30f - 25f * S(p);
            pose["ElbowR.x"] = p => -60f;
            return pose;
        }

        private static Pose HangIdlePose()
        {
            return new Pose
            {
                ["Hips.py"] = p => HipHeight,
                ["Hips.x"] = p => 5f * S(p),
                ["Torso.x"] = p => -2f * S(p),
                ["Head.x"] = p => -10f,
                ["ShoulderL.x"] = p => -172f, ["ShoulderR.x"] = p => -172f,
                ["ShoulderL.z"] = p => -12f, ["ShoulderR.z"] = p => 12f,
                ["ElbowL.x"] = p => -8f, ["ElbowR.x"] = p => -8f,
                ["HipL.x"] = p => -12f + 4f * S(p), ["HipR.x"] = p => -6f + 4f * S(p),
                ["KneeL.x"] = p => 25f, ["KneeR.x"] = p => 15f,
                ["AnkleL.x"] = p => 25f, ["AnkleR.x"] = p => 25f,
            };
        }

        /// <summary>Hand-over-hand along the bar. Played with negative speed for moving backwards.</summary>
        private static Pose HangForwardPose()
        {
            return new Pose
            {
                ["Hips.py"] = p => HipHeight,
                ["Hips.x"] = p => 8f * C(p),
                ["Hips.y"] = p => 14f * S(p),
                ["Torso.y"] = p => -6f * S(p),
                ["Head.x"] = p => -8f,
                ["ShoulderL.x"] = p => -165f - 22f * S(p), ["ShoulderR.x"] = p => -165f + 22f * S(p),
                ["ShoulderL.z"] = p => -10f, ["ShoulderR.z"] = p => 10f,
                ["ElbowL.x"] = p => -10f - 30f * Mathf.Max(0f, S(p)),
                ["ElbowR.x"] = p => -10f - 30f * Mathf.Max(0f, -S(p)),
                ["HipL.x"] = p => -15f + 18f * C(p), ["HipR.x"] = p => -15f + 18f * C(p + 0.8f),
                ["KneeL.x"] = p => 25f + 10f * S(p), ["KneeR.x"] = p => 25f - 10f * S(p),
                ["AnkleL.x"] = p => 25f, ["AnkleR.x"] = p => 25f,
            };
        }

        /// <summary>Left/right mirror: swap L and R joints and flip the Y and Z rotations.</summary>
        private static Pose Mirror(Pose source)
        {
            var mirrored = new Pose();
            foreach (var kv in source)
            {
                string[] parts = kv.Key.Split('.');
                string joint = parts[0], axis = parts[1];
                if (joint.EndsWith("L") && joint != "Hips") joint = joint.Substring(0, joint.Length - 1) + "R";
                else if (joint.EndsWith("R")) joint = joint.Substring(0, joint.Length - 1) + "L";
                Func<float, float> f = kv.Value;
                bool flip = axis == "y" || axis == "z" || axis == "px";
                mirrored[joint + "." + axis] = flip ? (p => -f(p)) : f;
            }
            return mirrored;
        }

        private static float Sq(float v) => v * v;

        private static AnimationClip SaveClip(string name, float length, Pose pose, bool loop = true)
        {
            var clip = new AnimationClip { name = name, frameRate = 30f };
            const int samples = 24;
            foreach (string joint in Joints)
            {
                foreach (string axis in new[] { "x", "y", "z" })
                {
                    Func<float, float> f = pose.TryGetValue(joint + "." + axis, out var g) ? g : (_ => 0f);
                    SetLoopCurve(clip, JointPaths[joint], "localEulerAnglesRaw." + axis, f, length, samples);
                }
            }
            SetLoopCurve(clip, "Hips", "m_LocalPosition.x", pose.TryGetValue("Hips.px", out var px) ? px : (_ => 0f), length, samples);
            SetLoopCurve(clip, "Hips", "m_LocalPosition.y", pose.TryGetValue("Hips.py", out var py) ? py : (_ => HipHeight), length, samples);
            SetLoopCurve(clip, "Hips", "m_LocalPosition.z", _ => 0f, length, samples);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            AssetDatabase.CreateAsset(clip, $"{AnimDir}/{name}.anim");
            return clip;
        }

        private static void SetLoopCurve(AnimationClip clip, string path, string property, Func<float, float> f,
                                         float length, int samples)
        {
            var keys = new Keyframe[samples + 1];
            const float e = 0.001f;
            for (int i = 0; i <= samples; i++)
            {
                float p = TwoPi * i / samples;
                float value = f(p);
                float slope = (f(p + e) - f(p - e)) / (2f * e) * (TwoPi / length);   // d/dt
                keys[i] = new Keyframe(length * i / samples, value, slope, slope);
            }
            var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), property);
            AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(keys));
        }

        // ================================================================ Animator Controller

        private static AnimatorController MakeController(Dictionary<string, AnimationClip> clips)
        {
            string path = RootDir + "/ObbyRunner.controller";
            AssetDatabase.DeleteAsset(path);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(path);

            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
            ac.AddParameter("IsJumping", AnimatorControllerParameterType.Bool);
            ac.AddParameter("VerticalVelocity", AnimatorControllerParameterType.Float);
            ac.AddParameter("IsWallRunning", AnimatorControllerParameterType.Bool);
            ac.AddParameter("WallSide", AnimatorControllerParameterType.Float);
            ac.AddParameter("IsHanging", AnimatorControllerParameterType.Bool);
            ac.AddParameter("HangSpeed", AnimatorControllerParameterType.Float);
            ac.AddParameter("Respawn", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("DoubleJump", AnimatorControllerParameterType.Trigger);

            // Start IsGrounded true so the character doesn't flash the fall pose on the first frame.
            var parameters = ac.parameters;
            foreach (var p in parameters) if (p.name == "IsGrounded") p.defaultBool = true;
            ac.parameters = parameters;

            AnimatorStateMachine sm = ac.layers[0].stateMachine;

            // Locomotion: Idle → Walk → Run → Sprint by speed (m/s).
            AnimatorState locomotion = ac.CreateBlendTreeInController("Locomotion", out BlendTree locoTree, 0);
            locoTree.blendType = BlendTreeType.Simple1D;
            locoTree.blendParameter = "Speed";
            locoTree.useAutomaticThresholds = false;
            locoTree.AddChild(clips["Idle"], 0f);
            locoTree.AddChild(clips["Walk"], 2f);
            locoTree.AddChild(clips["Run"], 5f);
            locoTree.AddChild(clips["Sprint"], 9f);

            AnimatorState jump = sm.AddState("Jump");
            jump.motion = clips["Jump"];
            AnimatorState fall = sm.AddState("Fall");
            fall.motion = clips["Fall"];

            AnimatorState wallRun = ac.CreateBlendTreeInController("WallRun", out BlendTree wallTree, 0);
            wallTree.blendType = BlendTreeType.Simple1D;
            wallTree.blendParameter = "WallSide";
            wallTree.useAutomaticThresholds = false;
            wallTree.AddChild(clips["WallRunLeft"], -1f);
            wallTree.AddChild(clips["WallRunRight"], 1f);

            AnimatorState hang = ac.CreateBlendTreeInController("Hang", out BlendTree hangTree, 0);
            hangTree.blendType = BlendTreeType.Simple1D;
            hangTree.blendParameter = "HangSpeed";
            hangTree.useAutomaticThresholds = false;
            hangTree.AddChild(clips["HangForward"], -1f);   // played backwards (timeScale -1 below)
            hangTree.AddChild(clips["HangIdle"], 0f);
            hangTree.AddChild(clips["HangForward"], 1f);
            var hangChildren = hangTree.children;
            hangChildren[0].timeScale = -1f;
            hangTree.children = hangChildren;

            sm.defaultState = locomotion;

            // Lay the states out tidily in the Animator window.
            SetStatePosition(sm, locomotion, new Vector3(300, 0));
            SetStatePosition(sm, jump, new Vector3(550, -120));
            SetStatePosition(sm, fall, new Vector3(550, 120));
            SetStatePosition(sm, wallRun, new Vector3(300, 260));
            SetStatePosition(sm, hang, new Vector3(50, 260));

            // Ground ↔ air
            Transition(locomotion, jump, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "IsJumping");
            var toFall = Transition(locomotion, fall, 0.15f);
            toFall.AddCondition(AnimatorConditionMode.IfNot, 0, "IsGrounded");
            toFall.AddCondition(AnimatorConditionMode.Less, -3f, "VerticalVelocity");   // ignore tiny ground flickers
            Transition(jump, locomotion, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "IsGrounded");
            Transition(jump, fall, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0, "IsJumping");
            Transition(fall, locomotion, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "IsGrounded");

            // Wallrun
            var anyToWall = sm.AddAnyStateTransition(wallRun);
            Configure(anyToWall, 0.15f);
            anyToWall.canTransitionToSelf = false;
            anyToWall.AddCondition(AnimatorConditionMode.If, 0, "IsWallRunning");
            var wallToJump = Transition(wallRun, jump, 0.12f);
            wallToJump.AddCondition(AnimatorConditionMode.IfNot, 0, "IsWallRunning");
            wallToJump.AddCondition(AnimatorConditionMode.If, 0, "IsJumping");
            Transition(wallRun, fall, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, "IsWallRunning");

            // Monkey bars
            var anyToHang = sm.AddAnyStateTransition(hang);
            Configure(anyToHang, 0.12f);
            anyToHang.canTransitionToSelf = false;
            anyToHang.AddCondition(AnimatorConditionMode.If, 0, "IsHanging");
            var hangToJump = Transition(hang, jump, 0.12f);
            hangToJump.AddCondition(AnimatorConditionMode.IfNot, 0, "IsHanging");
            hangToJump.AddCondition(AnimatorConditionMode.If, 0, "IsJumping");
            Transition(hang, fall, 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0, "IsHanging");

            // Double jump: a quick front flip from anywhere in the air, then into the fall.
            AnimatorState flip = sm.AddState("DoubleJump");
            flip.motion = clips["DoubleJumpFlip"];
            SetStatePosition(sm, flip, new Vector3(800, 0));
            var anyToFlip = sm.AddAnyStateTransition(flip);
            Configure(anyToFlip, 0.05f);
            anyToFlip.canTransitionToSelf = true;
            anyToFlip.AddCondition(AnimatorConditionMode.If, 0, "DoubleJump");
            var flipToFall = flip.AddTransition(fall);
            flipToFall.hasExitTime = true;
            flipToFall.exitTime = 0.95f;
            flipToFall.hasFixedDuration = true;
            flipToFall.duration = 0.1f;
            Transition(flip, locomotion, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "IsGrounded");

            // Respawn: snap straight back to standing.
            var anyToLoco = sm.AddAnyStateTransition(locomotion);
            Configure(anyToLoco, 0f);
            anyToLoco.canTransitionToSelf = true;
            anyToLoco.AddCondition(AnimatorConditionMode.If, 0, "Respawn");

            EditorUtility.SetDirty(ac);
            return ac;
        }

        private static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration)
        {
            var t = from.AddTransition(to);
            Configure(t, duration);
            return t;
        }

        private static void Configure(AnimatorStateTransition t, float duration)
        {
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = duration;
            t.offset = 0f;
        }

        private static void SetStatePosition(AnimatorStateMachine sm, AnimatorState state, Vector3 position)
        {
            var states = sm.states;
            for (int i = 0; i < states.Length; i++)
                if (states[i].state == state) states[i].position = position;
            sm.states = states;
        }

        // ================================================================ Preview sheet

        private static void RenderPreview(GameObject player, Dictionary<string, AnimationClip> clips, string path, StringBuilder report)
        {
            const int cell = 220, cols = 5;
            string[] order = { "Idle", "Walk", "Run", "Sprint", "Jump", "Fall", "WallRunLeft", "WallRunRight", "HangIdle", "HangForward", "DoubleJumpFlip" };
            Transform modelSource = player.transform.Find("Model");

            Vector3 origin = new Vector3(0f, -3000f, 0f);
            GameObject inst = UnityEngine.Object.Instantiate(modelSource.gameObject, origin, Quaternion.identity);
            var camGo = new GameObject("ObbyKit Preview Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.18f, 0.22f);
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 50f;
            var rt = new RenderTexture(cell, cell, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            var sheet = new Texture2D(cols * cell, order.Length * cell, TextureFormat.RGB24, false);

            try
            {
                for (int r = 0; r < order.Length; r++)
                {
                    AnimationClip clip = clips[order[r]];
                    for (int c = 0; c < cols; c++)
                    {
                        bool sideView = c == cols - 1;
                        float time = sideView ? clip.length * 0.25f : clip.length * c / (cols - 1);
                        clip.SampleAnimation(inst, time);

                        Vector3 look = origin + Vector3.up * (order[r].StartsWith("Hang") ? 1.3f : 1.0f);
                        Vector3 camPos = sideView ? origin + new Vector3(4.6f, 1.1f, 0f)          // from the character's right
                                                  : origin + new Vector3(3.0f, 1.5f, 3.4f);       // 3/4 front-right
                        cam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(look - camPos));
                        cam.Render();

                        RenderTexture.active = rt;
                        sheet.ReadPixels(new Rect(0, 0, cell, cell), c * cell, (order.Length - 1 - r) * cell);
                    }
                }
                sheet.Apply();
                File.WriteAllBytes(path, sheet.EncodeToPNG());
                report.AppendLine($"Preview: {path} — rows: {string.Join(", ", order)}; columns 1-4 = moments through the loop (3/4 view), column 5 = side view.");
            }
            finally
            {
                RenderTexture.active = null;
                cam.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(inst);
                UnityEngine.Object.DestroyImmediate(sheet);
            }
        }

        // ================================================================ Utils

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        private static string Fmt(Vector3 v) => $"({v.x:0.0}, {v.y:0.0}, {v.z:0.0})";
    }
}
