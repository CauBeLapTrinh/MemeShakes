using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Small pixel-art particle bursts (square, point-filtered particles that never rotate), built in code so no assets
    /// are needed. Every burst is its own short-lived object that removes itself.
    /// Sizes and speeds are given in cells, so the effects follow the board scale.
    /// </summary>
    public static class PixelFx
    {
        private const int SortingOrder = 30;

        private static Material material;
        private static Texture2D pixelTexture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            material = null;
            pixelTexture = null;
        }

        /// <summary>Little puff of dust behind a moving hand. <paramref name="direction"/> is the move direction.</summary>
        public static void Dust(Vector3 position, float cellSize, Vector2 direction)
        {
            Burst(position - (Vector3)(direction * cellSize * 0.35f), cellSize, new BurstSettings
            {
                ColorA = new Color(0.93f, 0.87f, 0.72f),
                ColorB = new Color(0.75f, 0.68f, 0.55f),
                Count = 6,
                Speed = new Vector2(0.3f, 0.9f),
                Lifetime = new Vector2(0.25f, 0.45f),
                Size = new Vector2(0.05f, 0.09f),
                Gravity = -0.05f,
                Radius = 0.12f,
                Direction = -direction,
                Spread = 70f
            });
        }

        /// <summary>Wooden chips flying off a pushed crate. <paramref name="direction"/> is the push direction.</summary>
        public static void Debris(Vector3 position, float cellSize, Vector2 direction)
        {
            Burst(position - (Vector3)(direction * cellSize * 0.4f), cellSize, new BurstSettings
            {
                ColorA = new Color(0.72f, 0.48f, 0.26f),
                ColorB = new Color(0.45f, 0.29f, 0.15f),
                Count = 10,
                Speed = new Vector2(1f, 2.4f),
                Lifetime = new Vector2(0.3f, 0.55f),
                Size = new Vector2(0.05f, 0.1f),
                Gravity = 1.2f,
                Radius = 0.2f,
                Direction = -direction + Vector2.up * 0.5f,
                Spread = 110f
            });
        }

        /// <summary>A crate smashed flat by a closing door: wooden chips fly out sideways, with a cloud of dust and a few dark flecks.</summary>
        public static void Smash(Vector3 position, float cellSize)
        {
            Burst(position, cellSize, new BurstSettings
            {
                ColorA = new Color(0.78f, 0.52f, 0.28f),
                ColorB = new Color(0.45f, 0.29f, 0.15f),
                Count = 18,
                Speed = new Vector2(1.5f, 3.6f),
                Lifetime = new Vector2(0.3f, 0.65f),
                Size = new Vector2(0.06f, 0.12f),
                Gravity = 1.8f,
                Radius = 0.22f,
                Direction = Vector2.up,
                Spread = 360f
            });

            Burst(position, cellSize, new BurstSettings
            {
                ColorA = new Color(0.93f, 0.87f, 0.72f),
                ColorB = new Color(0.7f, 0.64f, 0.52f),
                Count = 12,
                Speed = new Vector2(0.4f, 1.1f),
                Lifetime = new Vector2(0.35f, 0.6f),
                Size = new Vector2(0.08f, 0.14f),
                Gravity = -0.05f,
                Radius = 0.4f,
                Direction = Vector2.up,
                Spread = 360f,
                Shrink = true
            });

            Burst(position, cellSize, new BurstSettings
            {
                ColorA = new Color(0.25f, 0.25f, 0.3f),
                ColorB = new Color(0.1f, 0.1f, 0.12f),
                Count = 5,
                Speed = new Vector2(1f, 2f),
                Lifetime = new Vector2(0.25f, 0.45f),
                Size = new Vector2(0.04f, 0.07f),
                Gravity = 2f,
                Radius = 0.15f,
                Direction = Vector2.up,
                Spread = 360f
            });
        }

        /// <summary>Bright sparks where a closing door cuts an arm.</summary>
        public static void Sparks(Vector3 position, float cellSize)
        {
            Burst(position, cellSize, new BurstSettings
            {
                ColorA = new Color(1f, 0.95f, 0.55f),
                ColorB = new Color(1f, 0.45f, 0.15f),
                Count = 22,
                Speed = new Vector2(2f, 4.5f),
                Lifetime = new Vector2(0.2f, 0.45f),
                Size = new Vector2(0.04f, 0.08f),
                Gravity = 1.6f,
                Radius = 0.08f,
                Direction = Vector2.up,
                Spread = 360f
            });

            // A few dark chips of the door.
            Burst(position, cellSize, new BurstSettings
            {
                ColorA = new Color(0.25f, 0.25f, 0.3f),
                ColorB = new Color(0.1f, 0.1f, 0.12f),
                Count = 6,
                Speed = new Vector2(1f, 2f),
                Lifetime = new Vector2(0.3f, 0.5f),
                Size = new Vector2(0.06f, 0.1f),
                Gravity = 2f,
                Radius = 0.1f,
                Direction = Vector2.up,
                Spread = 160f
            });
        }

        /// <summary>Twinkle of coloured squares rising from a cell (switch pressed, cat selected).</summary>
        public static void Sparkle(Vector3 position, float cellSize, Color color)
        {
            Burst(position, cellSize, new BurstSettings
            {
                ColorA = color,
                ColorB = Color.Lerp(color, Color.white, 0.6f),
                Count = 10,
                Speed = new Vector2(0.4f, 1.2f),
                Lifetime = new Vector2(0.35f, 0.6f),
                Size = new Vector2(0.05f, 0.09f),
                Gravity = -0.3f,
                Radius = 0.3f,
                Direction = Vector2.up,
                Spread = 360f,
                Shrink = true
            });
        }

        /// <summary>Grey dust along the bottom of a door that opens or closes.</summary>
        public static void DoorDust(Vector3 position, float cellSize)
        {
            Burst(position + Vector3.down * cellSize * 0.35f, cellSize, new BurstSettings
            {
                ColorA = new Color(0.7f, 0.7f, 0.68f),
                ColorB = new Color(0.5f, 0.5f, 0.5f),
                Count = 8,
                Speed = new Vector2(0.4f, 1f),
                Lifetime = new Vector2(0.3f, 0.5f),
                Size = new Vector2(0.06f, 0.1f),
                Gravity = -0.1f,
                Radius = 0.35f,
                Direction = Vector2.up,
                Spread = 120f
            });
        }

        // ---------------------------------------------------------------- core

        private struct BurstSettings
        {
            public Color ColorA;
            public Color ColorB;
            public int Count;
            /// <summary>Min and max speed, in cells per second.</summary>
            public Vector2 Speed;
            public Vector2 Lifetime;
            /// <summary>Min and max particle size, in cells.</summary>
            public Vector2 Size;
            public float Gravity;
            /// <summary>Spawn radius, in cells.</summary>
            public float Radius;
            public Vector2 Direction;
            /// <summary>Opening angle around <see cref="Direction"/> in degrees; 360 sends particles everywhere.</summary>
            public float Spread;
            public bool Shrink;
        }

        private static void Burst(Vector3 position, float cellSize, BurstSettings settings)
        {
            var burstObject = new GameObject("PixelFx");
            burstObject.transform.position = position;

            // The emitter's local X axis points along the burst direction.
            Vector2 direction = settings.Direction.sqrMagnitude > 0.0001f ? settings.Direction.normalized : Vector2.up;
            burstObject.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);

            var particles = burstObject.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(settings.Lifetime.x, settings.Lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(settings.Speed.x * cellSize, settings.Speed.y * cellSize);
            main.startSize = new ParticleSystem.MinMaxCurve(settings.Size.x * cellSize, settings.Size.y * cellSize);
            main.startRotation = 0f;
            main.gravityModifier = settings.Gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(settings.ColorA, settings.ColorB);
            main.maxParticles = Mathf.Max(1, settings.Count);

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)settings.Count) });

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Max(0.001f, settings.Radius * cellSize);
            shape.radiusThickness = 1f;
            shape.arc = Mathf.Clamp(settings.Spread, 1f, 360f);
            // Centre the arc on the local X axis (the burst direction).
            shape.rotation = new Vector3(0f, 0f, settings.Spread >= 360f ? 0f : -settings.Spread * 0.5f);

            // Hard fade at the end, like a pixel game: full colour, then gone.
            ParticleSystem.ColorOverLifetimeModule fade = particles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);

            if (settings.Shrink)
            {
                ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
            }

            var particleRenderer = burstObject.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = GetMaterial();
            particleRenderer.sortingOrder = SortingOrder;
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;

            particles.Play();
            Object.Destroy(burstObject, settings.Lifetime.y + 0.3f);
        }

        private static Material GetMaterial()
        {
            if (material == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                material = new Material(shader) { name = "PixelFx", mainTexture = GetPixelTexture() };
            }

            return material;
        }

        /// <summary>A solid white square (a few texels, point filtered) so every particle is a crisp pixel.</summary>
        public static Texture2D GetPixelTexture()
        {
            if (pixelTexture == null)
            {
                pixelTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "PixelFxSquare"
                };

                var pixels = new Color32[16];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = new Color32(255, 255, 255, 255);
                }

                pixelTexture.SetPixels32(pixels);
                pixelTexture.Apply();
            }

            return pixelTexture;
        }
    }
}
