using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Catshakes
{
    /// <summary>
    /// Small code-only UI effects for feedback moments: a burst of pixel squares, a bounce ("punch") and a text that
    /// floats up and fades. The pieces are created on the canvas of the target, on top of everything, and remove
    /// themselves; a hidden runner plays them on unscaled time, so they also work while the game is paused.
    /// </summary>
    public static class UIFx
    {
        public static readonly Color[] GoldColors =
        {
            new Color32(255, 214, 74, 255), new Color32(255, 246, 196, 255), new Color32(255, 170, 51, 255), Color.white
        };

        public static readonly Color[] MintColors =
        {
            new Color32(126, 214, 168, 255), new Color32(214, 255, 226, 255), new Color32(255, 230, 120, 255), Color.white
        };

        /// <summary>Warm orange that reads on the cream boards and the green or yellow backgrounds.</summary>
        public static readonly Color TextColor = new Color32(255, 128, 24, 255);

        private sealed class Runner : MonoBehaviour
        {
        }

        private static Runner runner;

        // Running punches, so a second punch on the same target restarts from its real scale instead of growing it.
        private static readonly Dictionary<Transform, KeyValuePair<Coroutine, Vector3>> punches = new Dictionary<Transform, KeyValuePair<Coroutine, Vector3>>();

        /// <summary>Pixel squares fly out from the centre of <paramref name="target"/>, fall a little and fade.</summary>
        public static void Burst(RectTransform target, Color[] colors, int count = 18, float speed = 520f)
        {
            RectTransform layer = OverlayLayer(target);
            if (layer == null || colors == null || colors.Length == 0)
            {
                return;
            }

            Vector2 origin = LocalPointIn(layer, target);
            for (int i = 0; i < count; i++)
            {
                var piece = new GameObject("FxPixel", typeof(RectTransform), typeof(Image));
                var rect = (RectTransform)piece.transform;
                rect.SetParent(layer, false);
                rect.anchoredPosition = origin;
                float size = Random.Range(9f, 18f);
                rect.sizeDelta = new Vector2(size, size);

                var image = piece.GetComponent<Image>();
                image.color = colors[Random.Range(0, colors.Length)];
                image.raycastTarget = false;

                float angle = (i / (float)count + Random.Range(-0.04f, 0.04f)) * Mathf.PI * 2f;
                Vector2 velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed * Random.Range(0.55f, 1.1f);
                GetRunner().StartCoroutine(PixelRoutine(rect, image, velocity, Random.Range(0.55f, 0.85f)));
            }
        }

        /// <summary>Quick scale bounce of <paramref name="target"/> (it ends at its own scale).</summary>
        public static void Punch(Transform target, float amount = 0.18f, float duration = 0.35f)
        {
            if (target == null)
            {
                return;
            }

            Vector3 rest = target.localScale;
            if (punches.TryGetValue(target, out KeyValuePair<Coroutine, Vector3> running))
            {
                GetRunner().StopCoroutine(running.Key);
                rest = running.Value;
            }

            punches[target] = new KeyValuePair<Coroutine, Vector3>(GetRunner().StartCoroutine(PunchRoutine(target, rest, amount, duration)), rest);
        }

        /// <summary>A label that rises from <paramref name="target"/> and fades out.</summary>
        public static void FloatingText(RectTransform target, string text, Color color, float fontSize = 46f)
        {
            RectTransform layer = OverlayLayer(target);
            if (layer == null)
            {
                return;
            }

            var labelObject = new GameObject("FxText", typeof(RectTransform));
            var rect = (RectTransform)labelObject.transform;
            rect.SetParent(layer, false);
            rect.anchoredPosition = LocalPointIn(layer, target) + new Vector2(0f, 30f);
            rect.sizeDelta = new Vector2(600f, 80f);

            var label = labelObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = color;
            label.raycastTarget = false;
            label.outlineWidth = 0.3f;
            label.outlineColor = new Color32(40, 30, 20, 255);

            GetRunner().StartCoroutine(FloatRoutine(rect, label, 1.3f));
        }

        // ---------------------------------------------------------------- routines

        private static IEnumerator PixelRoutine(RectTransform rect, Image image, Vector2 velocity, float lifetime)
        {
            const float gravity = 900f;
            float elapsed = 0f;
            Color baseColor = image.color;
            float spin = Random.Range(-360f, 360f);

            while (elapsed < lifetime && rect != null)
            {
                float dt = Time.unscaledDeltaTime;
                elapsed += dt;
                velocity.y -= gravity * dt;
                velocity *= 1f - 2.2f * dt;
                rect.anchoredPosition += velocity * dt;
                rect.localRotation = Quaternion.Euler(0f, 0f, spin * elapsed);

                float fade = 1f - Mathf.Clamp01((elapsed - lifetime * 0.5f) / (lifetime * 0.5f));
                image.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * fade);
                yield return null;
            }

            if (rect != null)
            {
                Object.Destroy(rect.gameObject);
            }
        }

        private static IEnumerator PunchRoutine(Transform target, Vector3 rest, float amount, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration && target != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // Damped spring: big first swell, a small rebound, then rest.
                float wave = Mathf.Sin(t * Mathf.PI * 2.5f) * (1f - t);
                target.localScale = rest * (1f + amount * wave);
                yield return null;
            }

            if (target != null)
            {
                target.localScale = rest;
            }

            punches.Remove(target);
        }

        private static IEnumerator FloatRoutine(RectTransform rect, TMP_Text label, float lifetime)
        {
            Vector2 start = rect.anchoredPosition;
            float elapsed = 0f;
            while (elapsed < lifetime && rect != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / lifetime);
                rect.anchoredPosition = start + new Vector2(0f, 90f * (1f - (1f - t) * (1f - t)));
                float pop = t < 0.15f ? Mathf.Lerp(0.6f, 1.1f, t / 0.15f) : Mathf.Lerp(1.1f, 1f, (t - 0.15f) / 0.2f);
                rect.localScale = Vector3.one * pop;
                label.alpha = 1f - Mathf.Clamp01((t - 0.6f) / 0.4f);
                yield return null;
            }

            if (rect != null)
            {
                Object.Destroy(rect.gameObject);
            }
        }

        // ---------------------------------------------------------------- helpers

        private static Runner GetRunner()
        {
            if (runner == null)
            {
                var runnerObject = new GameObject("UIFxRunner") { hideFlags = HideFlags.HideInHierarchy };
                Object.DontDestroyOnLoad(runnerObject);
                runner = runnerObject.AddComponent<Runner>();
            }

            return runner;
        }

        /// <summary>A full-screen layer on top of the target's root canvas, where the effect pieces live.</summary>
        private static RectTransform OverlayLayer(RectTransform target)
        {
            Canvas canvas = target != null ? target.GetComponentInParent<Canvas>() : null;
            if (canvas == null)
            {
                return null;
            }

            canvas = canvas.rootCanvas;
            Transform existing = canvas.transform.Find("FxLayer");
            RectTransform layer;
            if (existing != null)
            {
                layer = (RectTransform)existing;
            }
            else
            {
                var layerObject = new GameObject("FxLayer", typeof(RectTransform));
                layer = (RectTransform)layerObject.transform;
                layer.SetParent(canvas.transform, false);
                layer.anchorMin = Vector2.zero;
                layer.anchorMax = Vector2.one;
                layer.offsetMin = Vector2.zero;
                layer.offsetMax = Vector2.zero;
            }

            layer.SetAsLastSibling();
            return layer;
        }

        /// <summary>The centre of <paramref name="target"/> in the local space of <paramref name="layer"/>.</summary>
        private static Vector2 LocalPointIn(RectTransform layer, RectTransform target)
        {
            Vector3 world = target.TransformPoint(target.rect.center);
            return layer.InverseTransformPoint(world);
        }
    }
}
