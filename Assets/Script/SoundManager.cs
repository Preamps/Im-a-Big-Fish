using UnityEngine;
using UnityEngine.Audio;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

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
    AmmoPickup,
    GunPickup,
    BlockadePurchase,

    // UI
    UISelect,
    UIConfirm,
    UIError,

    // Player States
    PlayerDown,
    ReviveComplete,
    PlayerJump,
    Footstep,

    // Ambience
    BackgroundMusic,

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
        [Range(0.5f, 2f)] public float pitchVariation = 1f;
        public bool is3D = false;
        public float maxDistance = 50f;
        public AudioMixerGroup mixerGroup; // Assign in Inspector (Master, Music, SFX)
    }

    // Audio Mixer References
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private string masterVolumeParam = "MasterVol";
    [SerializeField] private string musicVolumeParam = "MusicVol";
    [SerializeField] private string sfxVolumeParam = "SFXVol";

    private const string MasterVolumePrefsKey = "Audio.MasterVolume";
    private const string MusicVolumePrefsKey = "Audio.MusicVolume";
    private const string SfxVolumePrefsKey = "Audio.SfxVolume";

    [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;

    [SerializeField] private SoundClip[] soundClips;
    [SerializeField] private int maxConcurrentSounds = 128;
    [SerializeField] private bool allowPoolExpansion = true;
    [SerializeField] private int maxPoolSize = 192;
    [SerializeField] private int maxConcurrentGunSounds = 64; // Increased for multiplayer
    [SerializeField] private bool allowGunPoolExpansion = true;
    [SerializeField] private int maxGunPoolSize = 128; // Increased for multiplayer
    [SerializeField] private bool allowTransientGunVoices = true;
    [SerializeField] private int maxTransientGunVoices = 40; // Increased for multiplayer
    [SerializeField] private bool playBackgroundOnAwake = true;

    [Header("Gun Audio Enhancement")]
    [SerializeField] private float gunPitchVariationRange = 0.08f; // +/- variation on pitch
    [SerializeField] private float gunVolumeVariationRange = 0.05f; // +/- variation on volume
    [SerializeField] private float gunPunchVolume = 1.15f; // Boost volume for impact punch
    [SerializeField] private bool enableGunPitchVariation = true;
    [SerializeField] private bool enableGunVolumeVariation = true;

    [Header("Multiplayer Audio Management")]
    [SerializeField] private bool enableVoiceDucking = true;
    [SerializeField][Range(0.3f, 0.8f)] private float duckingThreshold = 0.7f; // Pool fullness to trigger ducking
    [SerializeField][Range(0.2f, 0.7f)] private float duckingVolume = 0.4f; // Volume when ducking
    [SerializeField] private bool enableDistancePriority = true;
    [SerializeField] private float gunSoundMaxDistance = 75f;
    [SerializeField] private float localPlayerVolumeBoost = 0.95f; // Slightly emphasize local shots without bypassing attenuation
    [SerializeField] private float remotePlayerVolumeScale = 0.85f; // Reduce remote player shots slightly

    private Dictionary<SoundType, SoundClip> soundDictionary;
    private List<AudioSource> audioSourcePool;
    private List<float> audioSourcePlayStartTime; // Track when each source started playing
    private List<AudioSource> gunAudioSourcePool;
    private List<float> gunAudioSourcePlayStartTime;
    private List<Vector3> gunAudioSourcePositions; // Track positions for distance-based priority
    private List<bool> gunAudioSourceLocked; // Prevent reuse of gunshot sounds until they finish
    private List<float> gunAudioSourceUnlockTime; // Track when each source should be unlocked
    private int activeTransientGunVoices = 0;

    private AudioSource musicSource;
    private AudioSource ambientSource;
    private readonly Dictionary<AudioSource, Coroutine> followSoundCoroutines = new Dictionary<AudioSource, Coroutine>();
    private readonly HashSet<string> missingMixerParamsWarned = new HashSet<string>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        InitializeAudioSources();
        InitializeGunAudioSources();
        InitializeSoundDictionary();
        LoadSavedMixerVolumes();
        InitializeMixerVolumes();

        if (playBackgroundOnAwake)
        {
            if (soundDictionary != null && soundDictionary.TryGetValue(SoundType.BackgroundMusic, out SoundClip bg))
            {
                if (bg.clip != null)
                {
                    musicSource.clip = bg.clip;
                    musicSource.volume = bg.volume;
                    musicSource.loop = true;
                    musicSource.Play();
                }
                else
                {
                    Debug.LogWarning("Background music clip is not assigned in SoundManager.");
                }
            }
        }
    }

    private void Update()
    {
        // Continuously clean up expired gun audio locks to prevent stuck states
        CleanupExpiredGunAudioLocks();

        // Protect locked gunshot sources by forcing them back to their original position
        // This prevents any other code from repositioning locked gunshot sounds
        ProtectLockedGunShotPositions();
    }

    private void InitializeAudioSources()
    {
        audioSourcePool = new List<AudioSource>();
        audioSourcePlayStartTime = new List<float>();

        for (int i = 0; i < maxConcurrentSounds; i++)
        {
            audioSourcePool.Add(CreatePooledAudioSource());
            audioSourcePlayStartTime.Add(0f);
        }

        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.loop = true;
        musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f;
        if (audioMixer != null)
        {
            musicSource.outputAudioMixerGroup = audioMixer.FindMatchingGroups("Music").Length > 0
                ? audioMixer.FindMatchingGroups("Music")[0]
                : null;
        }

        ambientSource = gameObject.AddComponent<AudioSource>();
        ambientSource.loop = true;
        ambientSource.playOnAwake = false;
        ambientSource.spatialBlend = 0f;
        if (audioMixer != null)
        {
            ambientSource.outputAudioMixerGroup = audioMixer.FindMatchingGroups("SFX").Length > 0
                ? audioMixer.FindMatchingGroups("SFX")[0]
                : null;
        }
    }

    private void InitializeGunAudioSources()
    {
        gunAudioSourcePool = new List<AudioSource>();
        gunAudioSourcePlayStartTime = new List<float>();
        gunAudioSourcePositions = new List<Vector3>();
        gunAudioSourceLocked = new List<bool>();
        gunAudioSourceUnlockTime = new List<float>();

        for (int i = 0; i < maxConcurrentGunSounds; i++)
        {
            AudioSource gunSource = CreatePooledAudioSource(1f);
            gunSource.priority = 16;
            gunAudioSourcePool.Add(gunSource);
            gunAudioSourcePlayStartTime.Add(0f);
            gunAudioSourcePositions.Add(Vector3.zero);
            gunAudioSourceLocked.Add(false);
            gunAudioSourceUnlockTime.Add(0f);
        }
    }

    private AudioSource CreatePooledAudioSource(float spatialBlend = 0.5f)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = spatialBlend;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.dopplerLevel = 0f;
        source.priority = 128;

        if (audioMixer != null)
        {
            source.outputAudioMixerGroup = audioMixer.FindMatchingGroups("SFX").Length > 0
                ? audioMixer.FindMatchingGroups("SFX")[0]
                : null;
        }

        return source;
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

    private void InitializeMixerVolumes()
    {
        if (audioMixer == null)
        {
            Debug.LogWarning("AudioMixer not assigned to SoundManager!");
            return;
        }

        ApplyMixerVolume(masterVolumeParam, masterVolume);
        ApplyMixerVolume(musicVolumeParam, musicVolume);
        ApplyMixerVolume(sfxVolumeParam, sfxVolume);
    }

    private void LoadSavedMixerVolumes()
    {
        masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumePrefsKey, masterVolume));
        musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumePrefsKey, musicVolume));
        sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumePrefsKey, sfxVolume));
    }

    private void SaveMixerVolume(string prefsKey, float volume)
    {
        PlayerPrefs.SetFloat(prefsKey, Mathf.Clamp01(volume));
        PlayerPrefs.Save();
    }

    private void ApplyMixerVolume(string parameterName, float volume)
    {
        if (audioMixer == null)
        {
            return;
        }

        TrySetMixerFloat(parameterName, VolumeTodB(volume));
    }

    private bool TrySetMixerFloat(string parameterName, float value)
    {
        if (audioMixer == null || string.IsNullOrWhiteSpace(parameterName))
        {
            return false;
        }

        if (!audioMixer.GetFloat(parameterName, out _))
        {
            if (!missingMixerParamsWarned.Contains(parameterName))
            {
                missingMixerParamsWarned.Add(parameterName);
                Debug.LogWarning($"AudioMixer exposed parameter not found: '{parameterName}'. Expose it in the mixer or update SoundManager parameter names in Inspector.");
            }
            return false;
        }

        audioMixer.SetFloat(parameterName, value);
        return true;
    }

    /// <summary>
    /// Convert 0-1 float volume to dB for AudioMixer
    /// </summary>
    private float VolumeTodB(float volume)
    {
        if (volume <= 0f) return -80f; // Silence threshold
        return 20f * Mathf.Log10(Mathf.Clamp01(volume));
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
        if (clip == null) return;

        AudioSource source = GetAvailableAudioSource();
        if (source == null) return;

        source.clip = clip;
        source.volume = Mathf.Clamp01(volume);
        source.pitch = pitch;

        if (is3D)
        {
            source.spatialBlend = 1f;
            source.maxDistance = maxDistance;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.transform.position = position;
        }
        else
        {
            source.spatialBlend = 0f;
        }

        source.Play();
    }

    /// <summary>
    /// Play gun sound with proper 3D settings. Supports overlapping PlayOneShot instances.
    /// Enhanced with pitch variation and volume punch for better impact.
    /// Locks the audio source to prevent position changes while gunshot is playing.
    /// </summary>
    public void PlayGunSound(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return;

        AudioSource source = GetAvailableGunAudioSource(preferNoSteal: true);
        if (source == null)
        {
            if (allowTransientGunVoices && activeTransientGunVoices < Mathf.Max(1, maxTransientGunVoices))
            {
                StartCoroutine(PlayTransientGunVoice(clip, position, volume, pitch));
            }
            return;
        }

        source.transform.position = position;
        source.spatialBlend = 1f;
        source.maxDistance = 75f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.dopplerLevel = 0f;
        source.priority = 16;

        // Apply pitch variation for organic feel
        float finalPitch = pitch;
        if (enableGunPitchVariation)
        {
            float pitchVariation = Random.Range(-gunPitchVariationRange, gunPitchVariationRange);
            finalPitch = pitch + pitchVariation;
            finalPitch = Mathf.Clamp(finalPitch, 0.5f, 2f);
        }
        source.pitch = finalPitch;

        // Apply volume variation and punch for impact
        float finalVolume = volume;
        if (enableGunVolumeVariation)
        {
            float volumeVariation = Random.Range(-gunVolumeVariationRange, gunVolumeVariationRange);
            finalVolume = (volume + volumeVariation) * gunPunchVolume;
        }
        else
        {
            finalVolume = volume * gunPunchVolume;
        }
        source.volume = Mathf.Clamp01(finalVolume);

        source.clip = clip;
        source.loop = false;

        // Lock the audio source to prevent repositioning
        int sourceIndex = gunAudioSourcePool.IndexOf(source);
        if (sourceIndex >= 0)
        {
            // CRITICAL: Save the gunshot position so it stays locked there
            gunAudioSourcePositions[sourceIndex] = position;
            gunAudioSourceLocked[sourceIndex] = true;
            // Set explicit unlock time based on clip duration + safety margin
            float clipDuration = clip.length / Mathf.Max(0.01f, finalPitch);
            float safetyMargin = 0.15f;
            gunAudioSourceUnlockTime[sourceIndex] = Time.time + clipDuration + safetyMargin;
            // Also start coroutine as backup safety measure
            StartCoroutine(UnlockGunAudioSourceWhenDone(sourceIndex, clipDuration));
        }

        source.Play();
    }

    /// <summary>
    /// Play gun sound with explicit pitch control (overrides random variation).
    /// Useful for weapons with specific pitch characteristics.
    /// Locks the audio source to prevent position changes while gunshot is playing.
    /// </summary>
    public void PlayGunSoundWithPitch(AudioClip clip, Vector3 position, float volume, float pitchOverride)
    {
        if (clip == null) return;

        AudioSource source = GetAvailableGunAudioSource(preferNoSteal: true);
        if (source == null)
        {
            if (allowTransientGunVoices && activeTransientGunVoices < Mathf.Max(1, maxTransientGunVoices))
            {
                StartCoroutine(PlayTransientGunVoice(clip, position, volume, pitchOverride));
            }
            return;
        }

        source.transform.position = position;
        source.spatialBlend = 1f;
        source.maxDistance = 75f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.dopplerLevel = 0f;
        source.priority = 16;
        source.pitch = Mathf.Clamp(pitchOverride, 0.5f, 2f);
        source.volume = Mathf.Clamp01(volume * gunPunchVolume);
        source.clip = clip;
        source.loop = false;

        // Lock the audio source to prevent repositioning
        int sourceIndex = gunAudioSourcePool.IndexOf(source);
        if (sourceIndex >= 0)
        {
            // CRITICAL: Save the gunshot position so it stays locked there
            gunAudioSourcePositions[sourceIndex] = position;
            gunAudioSourceLocked[sourceIndex] = true;
            // Set explicit unlock time based on clip duration + safety margin
            float clipDuration = clip.length / Mathf.Max(0.01f, source.pitch);
            float safetyMargin = 0.15f;
            gunAudioSourceUnlockTime[sourceIndex] = Time.time + clipDuration + safetyMargin;
            // Also start coroutine as backup safety measure
            StartCoroutine(UnlockGunAudioSourceWhenDone(sourceIndex, clipDuration));
        }

        source.Play();
    }

    /// <summary>
    /// Play gun sound optimized for multiplayer with local/remote player priority and distance-based ducking.
    /// Locks the audio source to prevent position changes while gunshot is playing.
    /// </summary>
    public void PlayGunSoundMultiplayer(AudioClip clip, Vector3 position, float volume, bool isLocalPlayer, Vector3 listenerPosition)
    {
        if (clip == null) return;

        // Apply local player boost or remote player reduction
        float finalVolume = volume;
        if (isLocalPlayer)
        {
            finalVolume *= localPlayerVolumeBoost;
        }
        else
        {
            finalVolume *= remotePlayerVolumeScale;
        }

        AudioSource source = GetAvailableGunAudioSource(preferNoSteal: true);
        if (source == null)
        {
            if (allowTransientGunVoices && activeTransientGunVoices < Mathf.Max(1, maxTransientGunVoices))
            {
                StartCoroutine(PlayTransientGunVoiceMultiplayer(clip, position, finalVolume, 1f, isLocalPlayer));
            }
            return;
        }

        if (isLocalPlayer)
        {
            // Keep the local shot in the same 3D mix as remote shots so it does not dominate the mix.
            source.spatialBlend = 1f;
            source.maxDistance = gunSoundMaxDistance;
            source.transform.position = position;
            source.priority = 12;
        }
        else
        {
            source.transform.position = position;
            source.spatialBlend = 1f;
            source.maxDistance = gunSoundMaxDistance;
            source.priority = 20;
        }
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.dopplerLevel = 0f;

        // Apply pitch variation
        float finalPitch = 1f;
        if (enableGunPitchVariation)
        {
            float pitchVariation = Random.Range(-gunPitchVariationRange, gunPitchVariationRange);
            finalPitch = 1f + pitchVariation;
            finalPitch = Mathf.Clamp(finalPitch, 0.5f, 2f);
        }
        source.pitch = finalPitch;

        // Apply volume variation and punch
        if (enableGunVolumeVariation)
        {
            float volumeVariation = Random.Range(-gunVolumeVariationRange, gunVolumeVariationRange);
            finalVolume = (finalVolume + volumeVariation) * gunPunchVolume;
        }
        else
        {
            finalVolume *= gunPunchVolume;
        }
        source.volume = Mathf.Clamp01(finalVolume);

        source.clip = clip;
        source.loop = false;

        // Track position and time for ducking
        int sourceIndex = gunAudioSourcePool.IndexOf(source);
        if (sourceIndex >= 0)
        {
            gunAudioSourcePositions[sourceIndex] = position;
            gunAudioSourceLocked[sourceIndex] = true;

            // Set explicit unlock time based on clip duration + safety margin
            float clipDuration = clip.length / Mathf.Max(0.01f, finalPitch);
            float safetyMargin = 0.15f;
            gunAudioSourceUnlockTime[sourceIndex] = Time.time + clipDuration + safetyMargin;

            // Also start coroutine as backup safety measure
            StartCoroutine(UnlockGunAudioSourceWhenDone(sourceIndex, clipDuration));
        }

        source.Play();

        // Apply voice ducking for multiplayer stress relief
        if (enableVoiceDucking)
        {
            ApplyVoiceDucking();
        }
    }

    /// <summary>
    /// Unlock a gun audio source after the sound finishes playing.
    /// Prevents other sounds from repositioning this source while gunshot is active.
    /// </summary>
    private IEnumerator UnlockGunAudioSourceWhenDone(int sourceIndex, float duration)
    {
        float elapsedTime = 0f;
        float safetyMargin = 0.1f;
        float totalWaitTime = duration + safetyMargin;

        while (elapsedTime < totalWaitTime && sourceIndex < gunAudioSourcePool.Count)
        {
            if (!gunAudioSourcePool[sourceIndex].isPlaying)
            {
                break;
            }
            yield return null;
            elapsedTime += Time.deltaTime;
        }

        // Ensure it's unlocked after playback
        if (sourceIndex < gunAudioSourceLocked.Count)
        {
            gunAudioSourceLocked[sourceIndex] = false;
        }
    }

    /// <summary>
    /// Apply volume reduction to older voices when pool is getting full.
    /// This prevents audio clipping in intense multiplayer moments.
    /// </summary>
    private void ApplyVoiceDucking()
    {
        float poolUsage = (float)gunAudioSourcePool.Count(s => s.isPlaying) / gunAudioSourcePool.Count;

        if (poolUsage >= duckingThreshold)
        {
            // Find oldest voices and reduce their volume
            for (int i = 0; i < gunAudioSourcePool.Count; i++)
            {
                if (gunAudioSourcePool[i].isPlaying && Time.time - gunAudioSourcePlayStartTime[i] > 0.05f)
                {
                    // Gradually reduce volume of older sources
                    float ageFactor = Mathf.Min(1f, (Time.time - gunAudioSourcePlayStartTime[i]) / 0.3f);
                    float targetVolume = Mathf.Lerp(gunAudioSourcePool[i].volume, duckingVolume, ageFactor * 0.5f);
                    gunAudioSourcePool[i].volume = targetVolume;
                }
            }
        }
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
        if (soundType != SoundType.BackgroundMusic)
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
        musicSource.volume = soundClip.volume;
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
        ambientSource.volume = soundClip.volume;
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
                StopFollowingRoutine(audioSourcePool[i]);
                audioSourcePlayStartTime[i] = Time.time;
                return audioSourcePool[i];
            }
        }

        // If all are busy, grow the pool before stealing an active source
        if (allowPoolExpansion && audioSourcePool.Count < Mathf.Max(maxConcurrentSounds, maxPoolSize))
        {
            AudioSource expandedSource = CreatePooledAudioSource();
            audioSourcePool.Add(expandedSource);
            audioSourcePlayStartTime.Add(Time.time);
            return expandedSource;
        }

        // Smart stealing: reuse the source that has been playing the longest
        float oldestPlayTime = audioSourcePlayStartTime[0];
        int oldestIndex = 0;

        for (int i = 1; i < audioSourcePool.Count; i++)
        {
            if (audioSourcePlayStartTime[i] < oldestPlayTime)
            {
                oldestPlayTime = audioSourcePlayStartTime[i];
                oldestIndex = i;
            }
        }

        AudioSource source = audioSourcePool[oldestIndex];
        StopFollowingRoutine(source);
        audioSourcePlayStartTime[oldestIndex] = Time.time;
        return source;
    }

    private AudioSource GetAvailableGunAudioSource(bool preferNoSteal = false)
    {
        // First, clean up any sources that should be unlocked
        CleanupExpiredGunAudioLocks();

        // Try to find an already-finished audio source that's not locked
        for (int i = 0; i < gunAudioSourcePool.Count; i++)
        {
            if (!gunAudioSourcePool[i].isPlaying && !gunAudioSourceLocked[i])
            {
                StopFollowingRoutine(gunAudioSourcePool[i]);
                gunAudioSourcePlayStartTime[i] = Time.time;

                // Reset position and rotation to prevent leftover positioning
                gunAudioSourcePool[i].transform.position = Vector3.zero;
                gunAudioSourcePool[i].transform.rotation = Quaternion.identity;

                return gunAudioSourcePool[i];
            }
        }

        // If all are busy, grow the pool
        if (allowGunPoolExpansion && gunAudioSourcePool.Count < Mathf.Max(maxConcurrentGunSounds, maxGunPoolSize))
        {
            AudioSource expandedSource = CreatePooledAudioSource(1f);
            gunAudioSourcePool.Add(expandedSource);
            gunAudioSourcePlayStartTime.Add(Time.time);
            gunAudioSourcePositions.Add(Vector3.zero);
            gunAudioSourceLocked.Add(false);
            gunAudioSourceUnlockTime.Add(0f);
            return expandedSource;
        }

        if (preferNoSteal)
        {
            return null;
        }

        // Smart stealing: reuse the source that has been playing the longest and is NOT locked
        float oldestPlayTime = float.MaxValue;
        int oldestIndex = -1;

        for (int i = 0; i < gunAudioSourcePool.Count; i++)
        {
            if (!gunAudioSourceLocked[i] && gunAudioSourcePlayStartTime[i] < oldestPlayTime)
            {
                oldestPlayTime = gunAudioSourcePlayStartTime[i];
                oldestIndex = i;
            }
        }

        if (oldestIndex < 0)
        {
            // All sources are locked, can't proceed
            return null;
        }

        AudioSource source = gunAudioSourcePool[oldestIndex];
        StopFollowingRoutine(source);
        gunAudioSourcePlayStartTime[oldestIndex] = Time.time;
        gunAudioSourceLocked[oldestIndex] = false;

        // Reset position and rotation when reusing
        source.transform.position = Vector3.zero;
        source.transform.rotation = Quaternion.identity;

        return source;
    }

    /// <summary>
    /// Force unlock any sources that are locked but have been silent for too long (safety cleanup).
    /// Uses explicit time-based unlocking to ensure locks don't get stuck.
    /// </summary>
    private void CleanupExpiredGunAudioLocks()
    {
        for (int i = 0; i < gunAudioSourcePool.Count; i++)
        {
            if (gunAudioSourceLocked[i])
            {
                // Unlock if the unlock time has passed
                if (Time.time >= gunAudioSourceUnlockTime[i])
                {
                    gunAudioSourceLocked[i] = false;
                }
                // Or unlock if source is not playing (safety fallback)
                else if (!gunAudioSourcePool[i].isPlaying)
                {
                    gunAudioSourceLocked[i] = false;
                }
            }
        }
    }

    /// <summary>
    /// Force locked gunshot sources to maintain their original position.
    /// This prevents any other code from repositioning them, even if the source is stolen or modified.
    /// This is the definitive fix for gunshot sounds teleporting to bullet hit positions.
    /// </summary>
    private void ProtectLockedGunShotPositions()
    {
        for (int i = 0; i < gunAudioSourcePool.Count; i++)
        {
            if (gunAudioSourceLocked[i])
            {
                // Force the source back to its saved position every frame
                gunAudioSourcePool[i].transform.position = gunAudioSourcePositions[i];
            }
        }
    }

    private IEnumerator PlayTransientGunVoice(AudioClip clip, Vector3 position, float volume, float pitch)
    {
        activeTransientGunVoices++;

        AudioSource tempSource = gameObject.AddComponent<AudioSource>();
        tempSource.playOnAwake = false;
        tempSource.spatialBlend = 1f;
        tempSource.maxDistance = 75f;
        tempSource.rolloffMode = AudioRolloffMode.Logarithmic;
        tempSource.dopplerLevel = 0f;
        tempSource.priority = 8;

        // Apply pitch variation for organic feel
        float finalPitch = pitch;
        if (enableGunPitchVariation)
        {
            float pitchVariation = Random.Range(-gunPitchVariationRange, gunPitchVariationRange);
            finalPitch = pitch + pitchVariation;
            finalPitch = Mathf.Clamp(finalPitch, 0.5f, 2f);
        }
        tempSource.pitch = finalPitch;

        // Apply volume variation and punch for impact
        float finalVolume = volume;
        if (enableGunVolumeVariation)
        {
            float volumeVariation = Random.Range(-gunVolumeVariationRange, gunVolumeVariationRange);
            finalVolume = (volume + volumeVariation) * gunPunchVolume;
        }
        else
        {
            finalVolume = volume * gunPunchVolume;
        }
        tempSource.volume = Mathf.Clamp01(finalVolume);
        tempSource.transform.position = position;

        if (audioMixer != null)
        {
            AudioMixerGroup[] sfxGroups = audioMixer.FindMatchingGroups("SFX");
            tempSource.outputAudioMixerGroup = sfxGroups.Length > 0 ? sfxGroups[0] : null;
        }

        tempSource.clip = clip;
        tempSource.loop = false;
        tempSource.Play();

        float life = clip.length / Mathf.Max(0.01f, Mathf.Abs(finalPitch));
        yield return new WaitForSeconds(life + 0.05f);

        if (tempSource != null)
        {
            Destroy(tempSource);
        }

        activeTransientGunVoices = Mathf.Max(0, activeTransientGunVoices - 1);
    }

    private IEnumerator PlayTransientGunVoiceMultiplayer(AudioClip clip, Vector3 position, float volume, float pitch, bool isLocalPlayer)
    {
        activeTransientGunVoices++;

        AudioSource tempSource = gameObject.AddComponent<AudioSource>();
        tempSource.playOnAwake = false;
        if (isLocalPlayer)
        {
            tempSource.spatialBlend = 0f;
            tempSource.maxDistance = 0f;
            tempSource.transform.position = Vector3.zero;
            tempSource.priority = 8;
        }
        else
        {
            tempSource.spatialBlend = 1f;
            tempSource.maxDistance = gunSoundMaxDistance;
            tempSource.transform.position = position;
            tempSource.priority = 16;
        }
        tempSource.rolloffMode = AudioRolloffMode.Logarithmic;
        tempSource.dopplerLevel = 0f;

        // Apply pitch variation for organic feel
        float finalPitch = pitch;
        if (enableGunPitchVariation)
        {
            float pitchVariation = Random.Range(-gunPitchVariationRange, gunPitchVariationRange);
            finalPitch = pitch + pitchVariation;
            finalPitch = Mathf.Clamp(finalPitch, 0.5f, 2f);
        }
        tempSource.pitch = finalPitch;

        // Apply volume variation and punch for impact
        float finalVolume = volume;
        if (enableGunVolumeVariation)
        {
            float volumeVariation = Random.Range(-gunVolumeVariationRange, gunVolumeVariationRange);
            finalVolume = (volume + volumeVariation) * gunPunchVolume;
        }
        else
        {
            finalVolume = volume * gunPunchVolume;
        }
        tempSource.volume = Mathf.Clamp01(finalVolume);
        if (!isLocalPlayer)
        {
            tempSource.transform.position = position;
        }

        if (audioMixer != null)
        {
            AudioMixerGroup[] sfxGroups = audioMixer.FindMatchingGroups("SFX");
            tempSource.outputAudioMixerGroup = sfxGroups.Length > 0 ? sfxGroups[0] : null;
        }

        tempSource.clip = clip;
        tempSource.loop = false;
        tempSource.Play();

        float life = clip.length / Mathf.Max(0.01f, Mathf.Abs(finalPitch));
        yield return new WaitForSeconds(life + 0.05f);

        if (tempSource != null)
        {
            Destroy(tempSource);
        }

        activeTransientGunVoices = Mathf.Max(0, activeTransientGunVoices - 1);
    }

    private void StopFollowingRoutine(AudioSource source)
    {
        if (followSoundCoroutines.TryGetValue(source, out Coroutine runningRoutine))
        {
            StopCoroutine(runningRoutine);
            followSoundCoroutines.Remove(source);
        }
    }

    private void ConfigureAudioSource(AudioSource source, SoundClip soundClip, bool is3D, Vector3 position)
    {
        source.clip = soundClip.clip;
        source.volume = soundClip.volume;

        // Apply pitch variation
        float pitch = soundClip.pitchVariation;
        source.pitch = 1f + Random.Range(-pitch * 0.1f, pitch * 0.1f);

        // Assign mixer group if available
        if (soundClip.mixerGroup != null)
        {
            source.outputAudioMixerGroup = soundClip.mixerGroup;
        }

        if (is3D)
        {
            source.spatialBlend = 1f;
            source.maxDistance = soundClip.maxDistance;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.transform.position = position;
        }
        else
        {
            source.spatialBlend = 0f;
        }
    }

    /// <summary>
    /// Set master volume via AudioMixer (0-1)
    /// </summary>
    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp01(volume);
        SaveMixerVolume(MasterVolumePrefsKey, masterVolume);
        ApplyMixerVolume(masterVolumeParam, masterVolume);
    }

    /// <summary>
    /// Set SFX volume via AudioMixer (0-1)
    /// </summary>
    public void SetSFXVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        SaveMixerVolume(SfxVolumePrefsKey, sfxVolume);
        ApplyMixerVolume(sfxVolumeParam, sfxVolume);
    }

    /// <summary>
    /// Set music volume via AudioMixer (0-1)
    /// </summary>
    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        SaveMixerVolume(MusicVolumePrefsKey, musicVolume);
        ApplyMixerVolume(musicVolumeParam, musicVolume);
    }

    public float GetMasterVolume()
    {
        return masterVolume;
    }

    public float GetSFXVolume()
    {
        return sfxVolume;
    }

    public float GetMusicVolume()
    {
        return musicVolume;
    }

    /// <summary>
    /// Mute/Unmute all sounds
    /// </summary>
    public void SetMuted(bool muted)
    {
        AudioListener.pause = muted;
    }

    /// <summary>
    /// Play gun sound while following a transform (e.g., for moving recoil effects)
    /// Uses preferNoSteal to protect locked gunshot sources from being repositioned.
    /// </summary>
    public void PlayGunSoundFollowingTransform(AudioClip clip, Transform followTransform, float volume = 1f, float pitch = 1f)
    {
        if (clip == null || followTransform == null) return;

        // Use preferNoSteal: true to prevent stealing locked gunshot sources
        AudioSource source = GetAvailableGunAudioSource(preferNoSteal: true);
        if (source == null)
        {
            // If no gun audio source available, create a transient one
            if (allowTransientGunVoices && activeTransientGunVoices < Mathf.Max(1, maxTransientGunVoices))
            {
                StartCoroutine(PlayTransientGunVoiceFollowingTransform(clip, followTransform, volume, pitch));
            }
            return;
        }

        source.volume = Mathf.Clamp01(volume);
        source.pitch = pitch;
        source.spatialBlend = 1f;
        source.maxDistance = 75f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.dopplerLevel = 0f;
        source.priority = 16;
        source.transform.position = followTransform.position;

        // Use PlayOneShot to allow overlapping
        source.PlayOneShot(clip, 1f);

        StopFollowingRoutine(source);
        followSoundCoroutines[source] = StartCoroutine(FollowTransformWhilePlaying(source, followTransform));
    }

    /// <summary>
    /// Play gun sound while following a transform using a transient source (not from pool).
    /// Used as fallback when pool is full.
    /// </summary>
    private IEnumerator PlayTransientGunVoiceFollowingTransform(AudioClip clip, Transform followTransform, float volume, float pitch)
    {
        if (followTransform == null || clip == null) yield break;

        activeTransientGunVoices++;
        AudioSource tempSource = gameObject.AddComponent<AudioSource>();
        tempSource.playOnAwake = false;
        tempSource.spatialBlend = 1f;
        tempSource.maxDistance = 75f;
        tempSource.rolloffMode = AudioRolloffMode.Logarithmic;
        tempSource.dopplerLevel = 0f;
        tempSource.priority = 16;
        tempSource.volume = Mathf.Clamp01(volume);
        tempSource.pitch = pitch;

        if (audioMixer != null)
        {
            AudioMixerGroup[] sfxGroups = audioMixer.FindMatchingGroups("SFX");
            tempSource.outputAudioMixerGroup = sfxGroups.Length > 0 ? sfxGroups[0] : null;
        }

        tempSource.PlayOneShot(clip, 1f);

        // Follow the transform while playing
        while (tempSource != null && tempSource.isPlaying && followTransform != null)
        {
            tempSource.transform.position = followTransform.position;
            yield return null;
        }

        if (tempSource != null)
        {
            Destroy(tempSource);
        }

        activeTransientGunVoices = Mathf.Max(0, activeTransientGunVoices - 1);
    }

    private IEnumerator FollowTransformWhilePlaying(AudioSource source, Transform followTransform)
    {
        // Wait a frame to let PlayOneShot start
        yield return null;

        while (source != null && source.isPlaying && followTransform != null)
        {
            source.transform.position = followTransform.position;
            yield return null;
        }

        // Cleanup
        if (source != null && followSoundCoroutines.ContainsKey(source))
        {
            followSoundCoroutines.Remove(source);
        }
    }
}
