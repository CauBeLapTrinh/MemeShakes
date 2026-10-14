using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Victory effect played where the two hands meet: a burst of confetti and an expanding ring.
    /// The prefab Assets/MainGame/Effects/WinEffect holds the ring sprite and the confetti particle system; when no
    /// prefab is assigned, an equivalent effect is generated in code. The object removes itself afterwards.
    /// </summary>
    public sealed class WinEffect : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer ring;
        [SerializeField] private ParticleSystem confetti;

        private static Sprite ringSprite;
        private static Texture2D dotTexture;

        private float ringStartScale;
        private float ringEndScale;
        private float ringDuration;
        private float elapsed;

        /// <summary>
        /// Spawns the effect at <paramref name="position"/>, from <paramref name="prefab"/> when given.
        /// <paramref name="duration"/> is the length of the ring animation.
        /// </summary>
        public static WinEffect Spawn(WinEffect prefab, Vector3 position, float cellSize, float duration)
        {
            WinEffect effect;
            if (prefab != null)
            {
                effect = Instantiate(prefab, position, Quaternion.identity);
            }
            else
            {
                effect = CreateGenerated(position);
            }

            effect.Begin(cellSize, Mathf.Max(0.2f, duration));
            return effect;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            ringSprite = null;
            dotTexture = null;
        }

        private static WinEffect CreateGenerated(Vector3 position)
        {
            var effectObject = new GameObject("WinEffect");
            effectObject.transform.position = position;
            WinEffect effect = effectObject.AddComponent<WinEffect>();

            var ringObject = new GameObject("Ring");
            ringObject.transform.SetParent(effectObject.transform, false);
            effect.ring = ringObject.AddComponent<SpriteRenderer>();
            effect.ring.sprite = GetRingSprite();

            var confettiObject = new GameObject("Confetti");
            confettiObject.transform.SetParent(effectObject.transform, false);
            effect.confetti = confettiObject.AddComponent<ParticleSystem>();

            var particleRenderer = confettiObject.GetComponent<ParticleSystemRenderer>();
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var material = new Material(shader);
                material.mainTexture = GetDotTexture();
                particleRenderer.material = material;
            }

            return effect;
        }

        private void Begin(float cellSize, float duration)
        {
            ringDuration = duration * 0.85f;
            ringStartScale = cellSize * 0.3f;
            ringEndScale = cellSize * 3.5f;

            if (ring != null)
            {
                ring.transform.localScale = Vector3.one * ringStartScale;
                ring.color = new Color(1f, 0.9f, 0.3f, 1f);
                ring.sortingOrder = 40;
            }

            if (confetti != null)
            {
                ConfigureConfetti(confetti, cellSize);
                confetti.Play();
            }

            Destroy(gameObject, duration + 1.5f);
        }

        private void Update()
        {
            if (ring == null)
            {
                return;
            }

            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / ringDuration);
            float eased = 1f - (1f - t) * (1f - t);

            ring.transform.localScale = Vector3.one * Mathf.Lerp(ringStartScale, ringEndScale, eased);
            Color color = ring.color;
            color.a = 1f - t;
            ring.color = color;
        }

        /// <summary>Sets up the confetti burst, scaled to the cell size. Used for the prefab and the generated effect alike.</summary>
        private static void ConfigureConfetti(ParticleSystem particles, float cellSize)
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.duration = 1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(cellSize * 2f, cellSize * 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(cellSize * 0.08f, cellSize * 0.18f);
            main.gravityModifier = 0.8f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var palette = new Gradient();
            palette.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.85f, 0.2f), 0f),
                    new GradientColorKey(new Color(1f, 0.4f, 0.6f), 0.33f),
                    new GradientColorKey(new Color(0.4f, 0.8f, 1f), 0.66f),
                    new GradientColorKey(new Color(0.6f, 1f, 0.5f), 1f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            var startColor = new ParticleSystem.MinMaxGradient(palette);
            startColor.mode = ParticleSystemGradientMode.RandomColor;
            main.startColor = startColor;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, 50),
                new ParticleSystem.Burst(0.2f, 30)
            });

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = cellSize * 0.15f;

            ParticleSystem.ColorOverLifetimeModule fade = particles.colorOverLifetime;
            fade.enabled = true;
            var fadeGradient = new Gradient();
            fadeGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(fadeGradient);

            var particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
            if (particleRenderer != null)
            {
                particleRenderer.sortingOrder = 50;
            }
        }

        private static Sprite GetRingSprite()
        {
            if (ringSprite == null)
            {
                Texture2D texture = CreateRingTexture();
                ringSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
            }

            return ringSprite;
        }

        private static Texture2D GetDotTexture()
        {
            if (dotTexture == null)
            {
                dotTexture = CreateDotTexture();
            }

            return dotTexture;
        }

        /// <summary>A soft white ring, one unit wide when used as a sprite (also saved as a png by the editor setup menu).</summary>
        public static Texture2D CreateRingTexture()
        {
            return CreateRadialTexture(128, distance => 1f - Mathf.Clamp01(Mathf.Abs(distance - 0.88f) / 0.07f));
        }

        /// <summary>A soft white dot for the confetti particles.</summary>
        public static Texture2D CreateDotTexture()
        {
            return CreateRadialTexture(32, distance => Mathf.Clamp01((1f - distance) * 6f));
        }

        private static Texture2D CreateRadialTexture(int size, System.Func<float, float> alphaAtDistance)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[size * size];
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius)) / radius;
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(alphaAtDistance(distance)) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
