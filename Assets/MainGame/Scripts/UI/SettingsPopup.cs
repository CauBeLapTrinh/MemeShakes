using UnityEngine;
using UnityEngine.UI;

namespace Catshakes
{
    /// <summary>
    /// Content of the Settings popup: the Music and SFX sliders. Values are loaded from and stored to
    /// PlayerPrefs through <see cref="AudioManager"/>, which also applies them to the audio sources.
    /// Show and hide the popup by activating / deactivating this object (<see cref="MenuUIManager"/> does it).
    /// </summary>
    public sealed class SettingsPopup : MonoBehaviour
    {
        [SerializeField] private Slider musicSlider;
        [SerializeField] private Slider sfxSlider;

        private void OnEnable()
        {
            if (musicSlider != null)
            {
                musicSlider.SetValueWithoutNotify(AudioManager.MusicVolume);
                musicSlider.onValueChanged.AddListener(HandleMusicChanged);
            }

            if (sfxSlider != null)
            {
                sfxSlider.SetValueWithoutNotify(AudioManager.SfxVolume);
                sfxSlider.onValueChanged.AddListener(HandleSfxChanged);
            }
        }

        private void OnDisable()
        {
            if (musicSlider != null)
            {
                musicSlider.onValueChanged.RemoveListener(HandleMusicChanged);
            }

            if (sfxSlider != null)
            {
                sfxSlider.onValueChanged.RemoveListener(HandleSfxChanged);
            }

            AudioManager.SaveSettings();
        }

        private void HandleMusicChanged(float value)
        {
            AudioManager.MusicVolume = value;
        }

        private void HandleSfxChanged(float value)
        {
            AudioManager.SfxVolume = value;
        }
    }
}
