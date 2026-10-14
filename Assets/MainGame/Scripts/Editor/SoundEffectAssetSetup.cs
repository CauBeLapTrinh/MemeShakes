using System.IO;
using UnityEditor;
using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Creates the sound and effect assets of the game inside Assets/MainGame:
    ///  - Sounds/Move.wav, Push.wav, Win.wav, Lose.wav, SwitchCat.wav, Cut.wav, Button.wav, Door.wav, Switch.wav
    ///    (placeholder sounds; replace them with your own files)
    ///  - Resources/SoundManager.prefab (loaded at runtime, holds the clips; empty clip fields are filled in)
    ///  - Effects/WinEffect.prefab with its ring sprite, dot texture and confetti material
    /// Existing files are kept, so running the menu again never overwrites sounds you replaced.
    /// </summary>
    public static class SoundEffectAssetSetup
    {
        private const string SoundFolder = "Assets/MainGame/Sounds";
        private const string EffectFolder = "Assets/MainGame/Effects";
        private const string ResourcesFolder = "Assets/MainGame/Resources";
        private const string SoundManagerPrefabPath = ResourcesFolder + "/SoundManager.prefab";
        private const string WinEffectPrefabPath = EffectFolder + "/WinEffect.prefab";

        [MenuItem("Catshakes/Setup Sound And Effect Assets")]
        public static void Setup()
        {
            EnsureFolder(SoundFolder);
            EnsureFolder(EffectFolder);
            EnsureFolder(ResourcesFolder);

            AudioClip move = EnsureSound(SoundId.Move);
            AudioClip push = EnsureSound(SoundId.Push);
            AudioClip win = EnsureSound(SoundId.Win);
            AudioClip lose = EnsureSound(SoundId.Lose);

            CreateSoundManagerPrefab(move, push, win, lose);
            FillMissingClips();
            CreateWinEffectPrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Sound and effect assets are in " + SoundFolder + ", " + EffectFolder + " and " + ResourcesFolder + ".");
        }

        // ---------------------------------------------------------------- sounds

        private static AudioClip EnsureSound(SoundId id)
        {
            string path = SoundFolder + "/" + id + ".wav";
            if (!File.Exists(path))
            {
                AudioClip generated = SoundManager.CreatePlaceholderClip(id);
                var samples = new float[generated.samples];
                generated.GetData(samples, 0);
                File.WriteAllBytes(path, EncodeWav16(samples, generated.frequency));
                Object.DestroyImmediate(generated);
                AssetDatabase.ImportAsset(path);
            }

            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        private static byte[] EncodeWav16(float[] samples, int sampleRate)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                int dataLength = samples.Length * 2;
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataLength);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });
                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataLength);

                for (int i = 0; i < samples.Length; i++)
                {
                    writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue));
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        private static void CreateSoundManagerPrefab(AudioClip move, AudioClip push, AudioClip win, AudioClip lose)
        {
            if (File.Exists(SoundManagerPrefabPath))
            {
                return;
            }

            var managerObject = new GameObject("SoundManager");
            SoundManager manager = managerObject.AddComponent<SoundManager>();

            var serialized = new SerializedObject(manager);
            serialized.FindProperty("moveClip").objectReferenceValue = move;
            serialized.FindProperty("pushClip").objectReferenceValue = push;
            serialized.FindProperty("winClip").objectReferenceValue = win;
            serialized.FindProperty("loseClip").objectReferenceValue = lose;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(managerObject, SoundManagerPrefabPath);
            Object.DestroyImmediate(managerObject);
        }

        /// <summary>Assigns a clip (generating its wav when needed) to every empty sound field of the SoundManager prefab.</summary>
        private static void FillMissingClips()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SoundManagerPrefabPath);
            SoundManager manager = prefab != null ? prefab.GetComponent<SoundManager>() : null;
            if (manager == null)
            {
                return;
            }

            var serialized = new SerializedObject(manager);
            var fields = new[]
            {
                ("switchCatClip", SoundId.SwitchCat),
                ("cutClip", SoundId.Cut),
                ("buttonClip", SoundId.Button),
                ("doorClip", SoundId.Door),
                ("switchClip", SoundId.Switch)
            };

            foreach ((string field, SoundId id) in fields)
            {
                SerializedProperty property = serialized.FindProperty(field);
                if (property != null && property.objectReferenceValue == null)
                {
                    property.objectReferenceValue = EnsureSound(id);
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
        }

        // ---------------------------------------------------------------- effects

        private static void CreateWinEffectPrefab()
        {
            if (File.Exists(WinEffectPrefabPath))
            {
                return;
            }

            string textureFolder = EffectFolder + "/Textures";
            EnsureFolder(textureFolder);
            Sprite ringSprite = EnsureSprite(textureFolder + "/WinRing.png", WinEffect.CreateRingTexture());
            Sprite dotSprite = EnsureSprite(textureFolder + "/ConfettiDot.png", WinEffect.CreateDotTexture());

            string materialPath = EffectFolder + "/Confetti.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Sprites/Default"));
                material.mainTexture = dotSprite.texture;
                AssetDatabase.CreateAsset(material, materialPath);
            }

            var root = new GameObject("WinEffect");
            WinEffect effect = root.AddComponent<WinEffect>();

            var ringObject = new GameObject("Ring");
            ringObject.transform.SetParent(root.transform, false);
            var ringRenderer = ringObject.AddComponent<SpriteRenderer>();
            ringRenderer.sprite = ringSprite;
            ringRenderer.sortingOrder = 40;

            var confettiObject = new GameObject("Confetti");
            confettiObject.transform.SetParent(root.transform, false);
            ParticleSystem particles = confettiObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            var particleRenderer = confettiObject.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = material;
            particleRenderer.sortingOrder = 50;

            var serialized = new SerializedObject(effect);
            serialized.FindProperty("ring").objectReferenceValue = ringRenderer;
            serialized.FindProperty("confetti").objectReferenceValue = particles;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, WinEffectPrefabPath);
            Object.DestroyImmediate(root);
        }

        /// <summary>Saves a pixel-art texture as a point-filtered sprite that is one unit wide.</summary>
        private static Sprite EnsureSprite(string path, Texture2D texture)
        {
            int pixelsPerUnit = texture.width;
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(path);

                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spritePixelsPerUnit = pixelsPerUnit;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            Object.DestroyImmediate(texture);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            int slash = folder.LastIndexOf('/');
            string parent = folder.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
        }
    }
}
