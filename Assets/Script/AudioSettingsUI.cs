using UnityEngine;
using UnityEngine.UI;

public class AudioSettingsUI : MonoBehaviour
{
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;

    private void Awake()
    {
        ConfigureSlider(musicSlider);
        ConfigureSlider(sfxSlider);
    }

    private void OnEnable()
    {
        SyncSlidersFromCurrentValues();
        HookListeners(true);
    }

    private void OnDisable()
    {
        HookListeners(false);
    }

    public void OnMusicSliderChanged(float value)
    {
        ApplyMusicVolume(value);
    }

    public void OnSfxSliderChanged(float value)
    {
        ApplySfxVolume(value);
    }

    private void ConfigureSlider(Slider slider)
    {
        if (slider == null)
        {
            return;
        }

        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
    }

    private void SyncSlidersFromCurrentValues()
    {
        if (musicSlider != null)
        {
            musicSlider.SetValueWithoutNotify(GetCurrentMusicVolume());
        }

        if (sfxSlider != null)
        {
            sfxSlider.SetValueWithoutNotify(GetCurrentSfxVolume());
        }
    }

    private void HookListeners(bool shouldHook)
    {
        if (musicSlider != null)
        {
            musicSlider.onValueChanged.RemoveListener(OnMusicSliderChanged);
            if (shouldHook)
            {
                musicSlider.onValueChanged.AddListener(OnMusicSliderChanged);
            }
        }

        if (sfxSlider != null)
        {
            sfxSlider.onValueChanged.RemoveListener(OnSfxSliderChanged);
            if (shouldHook)
            {
                sfxSlider.onValueChanged.AddListener(OnSfxSliderChanged);
            }
        }
    }

    private void ApplyMusicVolume(float value)
    {
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.SetMusicVolume(value);
            return;
        }

        PlayerPrefs.SetFloat("Audio.MusicVolume", Mathf.Clamp01(value));
        PlayerPrefs.Save();
    }

    private void ApplySfxVolume(float value)
    {
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.SetSFXVolume(value);
            return;
        }

        PlayerPrefs.SetFloat("Audio.SfxVolume", Mathf.Clamp01(value));
        PlayerPrefs.Save();
    }

    private float GetCurrentMusicVolume()
    {
        if (SoundManager.Instance != null)
        {
            return SoundManager.Instance.GetMusicVolume();
        }

        return Mathf.Clamp01(PlayerPrefs.GetFloat("Audio.MusicVolume", 1f));
    }

    private float GetCurrentSfxVolume()
    {
        if (SoundManager.Instance != null)
        {
            return SoundManager.Instance.GetSFXVolume();
        }

        return Mathf.Clamp01(PlayerPrefs.GetFloat("Audio.SfxVolume", 1f));
    }
}