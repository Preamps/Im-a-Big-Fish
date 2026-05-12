using UnityEngine;
using System.Collections.Generic;

public enum SoundType
{
    // Combat
    GunFire,
    Reload,
    BulletHit,
    BulletHitWall,
    EnemyDeath,
    PlayerDamage,
    PlayerDeath,

    // Waves
    WaveStart,
    WaveComplete,

    // Pickups & Economy
    MoneyPickup,
    AmmoPickup,
    GunPickup,
    BlockadePurchase,

    // UI
    UISelect,
    UIConfirm,
    UIError,

    // Ambience
    BackgroundMusic,
    WaveMusic
}

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [System.Serializable]
    public class SoundClip
    {
        public SoundType soundType;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        [Range(0.5f, 2f)] public float pitchVariation = 1f; // Use for random pitch
        public bool is3D = false;
        public float maxDistance = 50f;
    }

    [SerializeField] private SoundClip[] soundClips;
    [SerializeField] private int maxConcurrentSounds = 32;
    [SerializeField] private float masterVolume = 1f;
    [SerializeField] private float sfxVolume = 1f;
    [SerializeField] private float musicVolume = 1f;

    private Dictionary<SoundType, SoundClip> soundDictionary;
    private List<AudioSource> audioSourcePool;
    private int currentPoolIndex = 0;

    private AudioSource musicSource;
    private AudioSource ambientSource;

    private void Awake()
    {
        // Implement singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        InitializeAudioSources();
        InitializeSoundDictionary();
    }

    private void InitializeAudioSources()
    {
        audioSourcePool = new List<AudioSource>();

        // Create SFX audio sources
        for (int i = 0; i < maxConcurrentSounds; i++)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0.5f;
            audioSourcePool.Add(source);
        }

        // Create music source
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.loop = true;
        musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f;

        // Create ambient source
        ambientSource = gameObject.AddComponent<AudioSource>();
        ambientSource.loop = true;
        ambientSource.playOnAwake = false;
        ambientSource.spatialBlend = 0f;
    }

    private void InitializeSoundDictionary()
    {
        soundDictionary = new Dictionary<SoundType, SoundClip>();

        foreach (SoundClip sc in soundClips)
        {
            if (!soundDictionary.ContainsKey(sc.soundType))
            {
                soundDictionary.Add(sc.soundType, sc);
            }
        }
    }

    /// <summary>
    /// Play a sound effect at the world position (3D audio)
    /// </summary>
    public void PlaySound(SoundType soundType, Vector3 position)
    {
        if (!soundDictionary.TryGetValue(soundType, out SoundClip soundClip))
        {
            Debug.LogWarning($"Sound of type {soundType} not found!");
            return;
        }

        if (soundClip.clip == null)
        {
            Debug.LogWarning($"AudioClip for {soundType} is not assigned!");
            return;
        }

        AudioSource source = GetAvailableAudioSource();
        if (source == null) return;

        ConfigureAudioSource(source, soundClip, true, position);
        source.Play();
    }

    /// <summary>
    /// Play a sound effect globally (2D audio)
    /// </summary>
    public void PlaySound(SoundType soundType)
    {
        if (!soundDictionary.TryGetValue(soundType, out SoundClip soundClip))
        {
            Debug.LogWarning($"Sound of type {soundType} not found!");
            return;
        }

        if (soundClip.clip == null)
        {
            Debug.LogWarning($"AudioClip for {soundType} is not assigned!");
            return;
        }

        AudioSource source = GetAvailableAudioSource();
        if (source == null) return;

        ConfigureAudioSource(source, soundClip, false, Vector3.zero);
        source.Play();
    }

    public void PlayClip(AudioClip clip, Vector3 position, float volume = 1f, bool is3D = true, float pitch = 1f, float maxDistance = 50f)
    {
        if (clip == null)
        {
            return;
        }

        AudioSource source = GetAvailableAudioSource();
        if (source == null)
        {
            return;
        }

        source.clip = clip;
        source.volume = Mathf.Clamp01(volume) * sfxVolume * masterVolume;
        source.pitch = pitch;

        if (is3D)
        {
            source.spatialBlend = 1f;
            source.maxDistance = maxDistance;
            source.transform.position = position;
        }
        else
        {
            source.spatialBlend = 0f;
            source.transform.position = transform.position;
        }

        source.Play();
    }

    /// <summary>
    /// Play a custom audio clip at a world position (for gun-specific sounds, etc.)
    /// </summary>
    public void PlayGunSound(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
    {
        PlayClip(clip, position, volume, is3D: true, pitch: pitch, maxDistance: 50f);
    }

    /// <summary>
    /// Play a custom audio clip globally (for UI or global sounds)
    /// </summary>
    public void PlayGlobalClip(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        PlayClip(clip, Vector3.zero, volume, is3D: false, pitch: pitch);
    }

    /// <summary>
    /// Play music (loops automatically)
    /// </summary>
    public void PlayMusic(SoundType soundType)
    {
        if (soundType != SoundType.BackgroundMusic && soundType != SoundType.WaveMusic)
        {
            Debug.LogWarning($"{soundType} is not a music track!");
            return;
        }

        if (!soundDictionary.TryGetValue(soundType, out SoundClip soundClip))
        {
            Debug.LogWarning($"Music of type {soundType} not found!");
            return;
        }

        if (soundClip.clip == null)
        {
            Debug.LogWarning($"AudioClip for {soundType} is not assigned!");
            return;
        }

        musicSource.clip = soundClip.clip;
        musicSource.volume = soundClip.volume * musicVolume * masterVolume;
        musicSource.Play();
    }

    /// <summary>
    /// Stop music
    /// </summary>
    public void StopMusic()
    {
        if (musicSource.isPlaying)
        {
            musicSource.Stop();
        }
    }

    /// <summary>
    /// Play ambient sound (loops automatically)
    /// </summary>
    public void PlayAmbient(SoundType soundType)
    {
        if (!soundDictionary.TryGetValue(soundType, out SoundClip soundClip))
        {
            Debug.LogWarning($"Sound of type {soundType} not found!");
            return;
        }

        if (soundClip.clip == null)
        {
            Debug.LogWarning($"AudioClip for {soundType} is not assigned!");
            return;
        }

        ambientSource.clip = soundClip.clip;
        ambientSource.volume = soundClip.volume * sfxVolume * masterVolume;
        ambientSource.Play();
    }

    /// <summary>
    /// Stop ambient sound
    /// </summary>
    public void StopAmbient()
    {
        if (ambientSource.isPlaying)
        {
            ambientSource.Stop();
        }
    }

    private AudioSource GetAvailableAudioSource()
    {
        // Try to find an already-finished audio source
        for (int i = 0; i < audioSourcePool.Count; i++)
        {
            if (!audioSourcePool[i].isPlaying)
            {
                return audioSourcePool[i];
            }
        }

        // If all are busy, cycle through and restart the oldest one
        AudioSource source = audioSourcePool[currentPoolIndex];
        currentPoolIndex = (currentPoolIndex + 1) % audioSourcePool.Count;
        return source;
    }

    private void ConfigureAudioSource(AudioSource source, SoundClip soundClip, bool is3D, Vector3 position)
    {
        source.clip = soundClip.clip;
        source.volume = soundClip.volume * sfxVolume * masterVolume;

        // Apply slight pitch variation for variety
        float pitch = soundClip.pitchVariation;
        source.pitch = 1f + Random.Range(-pitch * 0.1f, pitch * 0.1f);

        if (is3D)
        {
            source.spatialBlend = 1f;
            source.maxDistance = soundClip.maxDistance;
            source.transform.position = position;
        }
        else
        {
            source.spatialBlend = 0f;
        }
    }

    /// <summary>
    /// Set master volume (0-1)
    /// </summary>
    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp01(volume);
        UpdateVolumes();
    }

    /// <summary>
    /// Set SFX volume (0-1)
    /// </summary>
    public void SetSFXVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        UpdateVolumes();
    }

    /// <summary>
    /// Set music volume (0-1)
    /// </summary>
    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        UpdateVolumes();
    }

    private void UpdateVolumes()
    {
        musicSource.volume = soundDictionary.ContainsKey(SoundType.BackgroundMusic)
            ? soundDictionary[SoundType.BackgroundMusic].volume * musicVolume * masterVolume
            : musicVolume * masterVolume;

        ambientSource.volume = sfxVolume * masterVolume;

        foreach (AudioSource source in audioSourcePool)
        {
            source.volume = sfxVolume * masterVolume;
        }
    }

    /// <summary>
    /// Mute/Unmute all sounds
    /// </summary>
    public void SetMuted(bool muted)
    {
        AudioListener.pause = muted;
    }
    public void PlayGunSoundFollowingTransform(AudioClip clip, Transform followTransform, float volume = 1f, float pitch = 1f)
    {
        if (clip == null || followTransform == null) return;

        GameObject soundObject = new GameObject("FollowingAudioSource");
        soundObject.transform.SetParent(followTransform);
        soundObject.transform.localPosition = Vector3.zero;

        AudioSource source = soundObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = Mathf.Clamp01(volume) * sfxVolume * masterVolume;
        source.pitch = pitch;
        source.spatialBlend = 1f;
        source.maxDistance = 50f;
        source.Play();

        Destroy(soundObject, clip.length);
    }
}