using TMPro;
using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Types the text of a TextMeshPro label letter by letter, from the start each time the object is enabled, and stops
    /// at the end. The whole text is laid out from the first frame (only the visible count grows), so lines never jump.
    /// Uses unscaled time.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    [DisallowMultipleComponent]
    public sealed class TypewriterText : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float charactersPerSecond = 30f;
        [Tooltip("Pause before the first letter appears.")]
        [SerializeField, Min(0f)] private float startDelay = 0.3f;
        [Tooltip("Plays a short \"talk\" blip (random pitch) every few letters.")]
        [SerializeField] private bool playTalkSound = true;
        [SerializeField, Min(1)] private int lettersPerBlip = 3;

        private TMP_Text label;
        private float elapsed;

        public bool IsFinished => label != null && label.maxVisibleCharacters >= label.textInfo.characterCount;

        private void Awake()
        {
            label = GetComponent<TMP_Text>();
        }

        private void OnEnable()
        {
            Restart();
        }

        private void OnDisable()
        {
            if (label != null)
            {
                label.maxVisibleCharacters = int.MaxValue;
            }
        }

        public void Restart()
        {
            if (label == null)
            {
                label = GetComponent<TMP_Text>();
            }

            elapsed = -startDelay;
            label.maxVisibleCharacters = 0;
            // Lay the text out now so the character count is known before the first letter shows.
            label.ForceMeshUpdate();
        }

        private void Update()
        {
            if (IsFinished)
            {
                return;
            }

            elapsed += Time.unscaledDeltaTime;
            int previous = label.maxVisibleCharacters;
            int visible = Mathf.Max(0, Mathf.FloorToInt(elapsed * charactersPerSecond));
            label.maxVisibleCharacters = visible;

            if (playTalkSound && visible / lettersPerBlip > previous / lettersPerBlip && visible <= label.textInfo.characterCount)
            {
                char letter = label.textInfo.characterInfo[Mathf.Max(0, visible - 1)].character;
                if (!char.IsWhiteSpace(letter))
                {
                    SoundManager.Play(SoundId.Talk, Random.Range(0.85f, 1.2f));
                }
            }
        }
    }
}
