using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Two paper-confetti cannons in the bottom corners of the screen that fire towards the middle: many colours and
    /// paper shapes (strips, squares, circles, triangles, stars, squiggles) that flip and drift down slowly.
    /// Everything is generated in code and sized from the camera, so it fits any board. The object removes itself.
    /// </summary>
    public sealed class ConfettiCannons : MonoBehaviour
    {
        private const int ShapeCells = 4;     // atlas is ShapeCells x ShapeCells
        private const int CellPixels = 16;
        private const int ShapeCount = 8;
        private const float Lifetime = 3.2f;

        private static readonly Color[] Palette =
        {
            new Color32(255, 214, 51, 255),  // yellow
            new Color32(255, 92, 141, 255),  // pink
            new Color32(77, 196, 255, 255),  // sky blue
            new Color32(120, 230, 100, 255), // green
            new Color32(255, 140, 50, 255),  // orange
            new Color32(170, 110, 255, 255), // purple
            new Color32(255, 70, 70, 255),   // red
            new Color32(255, 255, 255, 255)  // white
        };

        private static Material material;

        /// <summary>Fires both cannons from the bottom corners of <paramref name="camera"/>'s view.</summary>
        public static ConfettiCannons Fire(Camera camera, int sortingOrder = 200)
        {
            if (camera == null)
            {
                return null;
            }

            var root = new GameObject("ConfettiCannons");
            ConfettiCannons cannons = root.AddComponent<ConfettiCannons>();

            float depth = camera.orthographic ? camera.nearClipPlane + 1f : 10f;
            Vector3 bottomLeft = camera.ViewportToWorldPoint(new Vector3(0f, 0f, depth));
            Vector3 topRight = camera.ViewportToWorldPoint(new Vector3(1f, 1f, depth));
            float screenHeight = Mathf.Abs(topRight.y - bottomLeft.y);
            float screenWidth = Mathf.Abs(topRight.x - bottomLeft.x);

            // Just outside the corners, so the paper bursts in from off-screen.
            Vector3 margin = new Vector3(screenWidth * 0.02f, screenHeight * 0.04f, 0f);
            CreateCannon(root.transform, new Vector3(bottomLeft.x - margin.x, bottomLeft.y - margin.y, bottomLeft.z), 62f, screenHeight, sortingOrder);
            CreateCannon(root.transform, new Vector3(topRight.x + margin.x, bottomLeft.y - margin.y, bottomLeft.z), 180f - 62f, screenHeight, sortingOrder);

            Destroy(root, Lifetime + 1.5f);
            return cannons;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            material = null;
        }

        /// <summary>One cannon at <paramref name="position"/> aimed at <paramref name="angleDegrees"/> (0 = right, 90 = up).</summary>
        private static void CreateCannon(Transform parent, Vector3 position, float angleDegrees, float screenHeight, int sortingOrder)
        {
            var cannonObject = new GameObject("Cannon");
            cannonObject.transform.SetParent(parent, false);
            cannonObject.transform.position = position;

            // A cone shoots along its local +Z: turn +Z towards the aim inside the screen plane.
            float radians = angleDegrees * Mathf.Deg2Rad;
            var aim = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f);
            cannonObject.transform.rotation = Quaternion.LookRotation(aim, Vector3.back);

            ParticleSystem particles = cannonObject.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // Speeds and gravity scale with the screen, so the paper reaches the middle on any camera size.
            float gravity = screenHeight * 0.55f;

            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(Lifetime * 0.7f, Lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(screenHeight * 0.9f, screenHeight * 1.6f);
            main.startSize3D = false;
            main.startSize = new ParticleSystem.MinMaxCurve(screenHeight * 0.03f, screenHeight * 0.055f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity / 9.81f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 400;
            main.startColor = PaletteColors();

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, 90),
                new ParticleSystem.Burst(0.15f, 60),
                new ParticleSystem.Burst(0.3f, 40)
            });

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 16f;
            shape.radius = screenHeight * 0.02f;

            // Air drag: the burst slows down quickly, then the paper floats down.
            ParticleSystem.LimitVelocityOverLifetimeModule drag = particles.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = new ParticleSystem.MinMaxCurve(float.MaxValue);
            drag.drag = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            drag.multiplyDragByParticleSize = false;
            drag.multiplyDragByParticleVelocity = false;

            // Side to side sway while falling.
            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(screenHeight * 0.12f);
            noise.frequency = 0.6f;
            noise.scrollSpeed = 0.4f;
            noise.damping = false;

            // Paper flipping in the air: spin on every axis.
            ParticleSystem.RotationOverLifetimeModule spin = particles.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-8f, 8f);
            spin.y = new ParticleSystem.MinMaxCurve(-8f, 8f);
            spin.z = new ParticleSystem.MinMaxCurve(-4f, 4f);

            ParticleSystem.ColorOverLifetimeModule fade = particles.colorOverLifetime;
            fade.enabled = true;
            var fadeGradient = new Gradient();
            fadeGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(fadeGradient);

            // One random paper shape per particle from the generated atlas.
            ParticleSystem.TextureSheetAnimationModule sheet = particles.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = ShapeCells;
            sheet.numTilesY = ShapeCells;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, (ShapeCount - 0.01f) / (ShapeCells * ShapeCells));
            sheet.cycleCount = 1;

            var particleRenderer = cannonObject.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.sharedMaterial = GetMaterial();
            particleRenderer.sortingOrder = sortingOrder;

            particles.Play();
        }

        /// <summary>
        /// Each particle gets exactly one palette colour: a fixed (stepped) gradient sampled at random, so colours never
        /// blend into each other.
        /// </summary>
        private static ParticleSystem.MinMaxGradient PaletteColors()
        {
            var keys = new GradientColorKey[Palette.Length];
            for (int i = 0; i < Palette.Length; i++)
            {
                keys[i] = new GradientColorKey(Palette[i], (i + 1f) / Palette.Length);
            }

            var gradient = new Gradient { mode = GradientMode.Fixed };
            gradient.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };
        }

        private static Material GetMaterial()
        {
            if (material == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                material = new Material(shader) { name = "ConfettiPaper", mainTexture = CreateShapeAtlas() };
            }

            return material;
        }

        /// <summary>
        /// White paper shapes in a ShapeCells x ShapeCells pixel-art atlas: strip, square, circle, triangle, star,
        /// squiggle, diamond and thin ribbon. The particle colour tints them.
        /// </summary>
        private static Texture2D CreateShapeAtlas()
        {
            int size = ShapeCells * CellPixels;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "ConfettiShapes",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[size * size];
            for (int shapeIndex = 0; shapeIndex < ShapeCount; shapeIndex++)
            {
                // Texture sheet frames run left to right, top row first.
                int cellX = shapeIndex % ShapeCells;
                int cellY = ShapeCells - 1 - shapeIndex / ShapeCells;

                for (int y = 0; y < CellPixels; y++)
                {
                    for (int x = 0; x < CellPixels; x++)
                    {
                        // -1..1 inside the cell.
                        float u = (x + 0.5f) / CellPixels * 2f - 1f;
                        float v = (y + 0.5f) / CellPixels * 2f - 1f;
                        if (InsideShape(shapeIndex, u, v))
                        {
                            pixels[(cellY * CellPixels + y) * size + cellX * CellPixels + x] = new Color32(255, 255, 255, 255);
                        }
                    }
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static bool InsideShape(int shapeIndex, float u, float v)
        {
            switch (shapeIndex)
            {
                case 0: // long strip
                    return Mathf.Abs(u) <= 0.85f && Mathf.Abs(v) <= 0.32f;
                case 1: // square
                    return Mathf.Abs(u) <= 0.6f && Mathf.Abs(v) <= 0.6f;
                case 2: // circle
                    return u * u + v * v <= 0.62f;
                case 3: // triangle
                    return v >= -0.65f && v <= 0.75f && Mathf.Abs(u) <= (0.75f - v) * 0.6f;
                case 4: // star
                {
                    float angle = Mathf.Atan2(v, u);
                    float radius = Mathf.Sqrt(u * u + v * v);
                    float edge = Mathf.Lerp(0.4f, 0.9f, Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 2.5f + Mathf.PI * 0.25f)), 3f));
                    return radius <= edge;
                }
                case 5: // squiggle ribbon
                    return Mathf.Abs(u) <= 0.9f && Mathf.Abs(v - Mathf.Sin(u * Mathf.PI * 1.5f) * 0.4f) <= 0.2f;
                case 6: // diamond
                    return Mathf.Abs(u) * 0.8f + Mathf.Abs(v) <= 0.7f;
                default: // thin ribbon
                    return Mathf.Abs(u) <= 0.9f && Mathf.Abs(v) <= 0.15f;
            }
        }
    }
}
