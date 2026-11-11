// =====================================================================================
//  DoubleJumpIndicator.cs  —  ObbyKit
// -------------------------------------------------------------------------------------
//  On-screen icon showing whether the player's double jump is ready.
//   • Green double-arrow icon with a soft pulsing glow + "READY" = you can double jump.
//   • Grey, slightly shrunk + "USED" = spent. Lands, wallruns, bar grabs and respawns refill it.
//   • A little "pop" animation the moment it becomes ready again.
//   • If PlayerController has a Double Jump Cooldown, a radial sweep shows the cooldown.
//
//  UNITY SETUP
//  Add this component to the player (ObbyPlayer already has it). It builds its own
//  Screen Space - Overlay canvas at runtime, so there are no UI assets to set up.
//  Change the corner offset, size and colours in the Inspector. The icon hides itself
//  if double jump is switched off on the PlayerController.
//  Requires the built-in "Unity UI" package (com.unity.ugui), which is in every new project.
// =====================================================================================

using UnityEngine;
using UnityEngine.UI;

namespace ObbyKit
{
    [DisallowMultipleComponent]
    public class DoubleJumpIndicator : MonoBehaviour
    {
        [Header("References (auto-found if empty)")]
        [SerializeField] private PlayerController player;

        [Header("Layout (pixels at 1920×1080)")]
        [Tooltip("Distance from the bottom-left corner of the screen.")]
        [SerializeField] private Vector2 screenOffset = new Vector2(56f, 72f);
        [SerializeField] private float iconSize = 96f;
        [SerializeField] private bool showLabel = true;

        [Header("Colours")]
        [SerializeField] private Color readyColor = new Color(0.25f, 0.9f, 0.5f, 1f);
        [SerializeField] private Color usedColor = new Color(0.55f, 0.58f, 0.63f, 0.8f);
        [SerializeField] private Color backgroundColor = new Color(0.06f, 0.08f, 0.12f, 0.65f);

        private GameObject canvasGo;
        private RectTransform iconRoot;
        private Image glow, glyph, cooldownFill;
        private Text label;
        private bool wasReady;
        private float popTimer;
        private float scaleCurrent = 1f;

        private static Sprite discSprite, glyphSprite, glowSprite;

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (player == null) player = FindObjectOfType<PlayerController>();
            BuildUI();
        }

        private void OnEnable() { if (canvasGo != null) canvasGo.SetActive(true); }
        private void OnDisable() { if (canvasGo != null) canvasGo.SetActive(false); }
        private void OnDestroy() { if (canvasGo != null) Destroy(canvasGo); }

        private void Update()
        {
            if (player == null || canvasGo == null) return;

            bool show = player.DoubleJumpEnabled && player.isActiveAndEnabled;
            if (canvasGo.activeSelf != show) canvasGo.SetActive(show);
            if (!show) return;

            float dt = Time.unscaledDeltaTime;
            bool ready = player.DoubleJumpReady;
            if (ready && !wasReady) popTimer = 1f;      // just became ready, so play the pop
            wasReady = ready;
            popTimer = Mathf.Max(0f, popTimer - dt / 0.35f);

            // Colour
            Color target = ready ? readyColor : usedColor;
            float k = 1f - Mathf.Exp(-18f * dt);
            glyph.color = Color.Lerp(glyph.color, target, k);

            // Glow pulse while ready
            float glowAlpha = ready ? 0.35f + 0.2f * Mathf.Sin(Time.unscaledTime * 4f) + 0.45f * popTimer : 0f;
            Color g = readyColor;
            g.a = Mathf.Lerp(glow.color.a, glowAlpha, 1f - Mathf.Exp(-12f * dt));
            glow.color = g;

            // Scale: shrink when used, quick overshoot "pop" when it comes back
            scaleCurrent = Mathf.Lerp(scaleCurrent, ready ? 1f : 0.86f, 1f - Mathf.Exp(-14f * dt));
            float pop = popTimer > 0f ? Mathf.Sin((1f - popTimer) * Mathf.PI) * 0.28f : 0f;
            iconRoot.localScale = Vector3.one * (scaleCurrent + pop);

            // Cooldown sweep (only when a cooldown is set)
            bool showSweep = !ready && player.DoubleJumpCooldown > 0f && player.DoubleJumpCooldownProgress < 1f;
            cooldownFill.enabled = showSweep;
            if (showSweep) cooldownFill.fillAmount = player.DoubleJumpCooldownProgress;

            if (label != null)
            {
                label.text = !ready ? "USED" : player.AirJumpsLeft > 1 ? $"READY x{player.AirJumpsLeft}" : "READY";
                label.color = glyph.color;
            }
        }

        // ================================================================ UI construction
        private void BuildUI()
        {
            EnsureSprites();

            canvasGo = new GameObject("DoubleJump HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            iconRoot = NewRect("Double Jump Icon", canvasGo.transform);
            iconRoot.anchorMin = iconRoot.anchorMax = Vector2.zero;
            iconRoot.pivot = new Vector2(0.5f, 0.5f);
            iconRoot.anchoredPosition = screenOffset + Vector2.one * (iconSize * 0.5f);
            iconRoot.sizeDelta = Vector2.one * iconSize;

            glow = NewImage("Glow", iconRoot, glowSprite, iconSize * 1.6f, new Color(readyColor.r, readyColor.g, readyColor.b, 0f));
            NewImage("Background", iconRoot, discSprite, iconSize, backgroundColor);
            cooldownFill = NewImage("Cooldown", iconRoot, discSprite, iconSize * 0.84f, new Color(1f, 1f, 1f, 0.18f));
            cooldownFill.type = Image.Type.Filled;
            cooldownFill.fillMethod = Image.FillMethod.Radial360;
            cooldownFill.fillOrigin = (int)Image.Origin360.Top;
            cooldownFill.fillClockwise = true;
            cooldownFill.enabled = false;
            glyph = NewImage("Glyph", iconRoot, glyphSprite, iconSize, readyColor);

            if (showLabel)
            {
                var labelRect = NewRect("Label", iconRoot);
                labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0f);
                labelRect.pivot = new Vector2(0.5f, 1f);
                labelRect.anchoredPosition = new Vector2(0f, -4f);
                labelRect.sizeDelta = new Vector2(200f, 30f);
                label = labelRect.gameObject.AddComponent<Text>();
                label.font = LoadFont();
                label.fontSize = 20;
                label.fontStyle = FontStyle.Bold;
                label.alignment = TextAnchor.UpperCenter;
                label.raycastTarget = false;
                label.text = "READY";
                label.color = readyColor;
                var shadow = labelRect.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
                shadow.effectDistance = new Vector2(1.5f, -1.5f);
            }
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        private static Image NewImage(string name, RectTransform parent, Sprite sprite, float size, Color color)
        {
            var rt = NewRect(name, parent);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.one * size;
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static Font LoadFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { /* older Unity */ }
            if (f == null) { try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
            return f;
        }

        // ================================================================ Procedural icon art
        // Drawn once at runtime with anti-aliased distance maths, so no texture files are needed.
        private static void EnsureSprites()
        {
            const int size = 128;
            if (discSprite == null)
                discSprite = MakeSprite("DJ_Disc", size, (u, v, px) => Edge(0.96f - Mathf.Sqrt(u * u + v * v), px));

            if (glowSprite == null)
                glowSprite = MakeSprite("DJ_Glow", size, (u, v, px) =>
                {
                    float d = Mathf.Sqrt(u * u + v * v);
                    return Mathf.Pow(Mathf.Clamp01(1f - d), 1.8f);
                });

            if (glyphSprite == null)
                glyphSprite = MakeSprite("DJ_Glyph", size, (u, v, px) =>
                {
                    float d = Mathf.Sqrt(u * u + v * v);
                    float ring = Edge(0.04f - Mathf.Abs(d - 0.84f), px);
                    var p = new Vector2(u, v);
                    // Two stacked chevrons (^ ^), i.e. "jump, then jump again".
                    float c1 = Chevron(p, -0.36f), c2 = Chevron(p, 0.02f);
                    float chev = Edge(0.075f - Mathf.Min(c1, c2), px);
                    return Mathf.Max(ring, chev);
                });
        }

        private static float Chevron(Vector2 p, float baseY)
        {
            var left = new Vector2(-0.36f, baseY);
            var apex = new Vector2(0f, baseY + 0.32f);
            var right = new Vector2(0.36f, baseY);
            return Mathf.Min(SegmentDistance(p, left, apex), SegmentDistance(p, apex, right));
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        private static float Edge(float signedInside, float px) => Mathf.Clamp01(signedInside / px + 0.5f);

        private static Sprite MakeSprite(string name, int size, System.Func<float, float, float, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            float px = 2f / size;   // one pixel in -1..1 space
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    byte a = (byte)(Mathf.Clamp01(alpha(u, v, px)) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
