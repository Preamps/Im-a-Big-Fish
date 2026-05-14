using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class Gun : NetworkBehaviour
{
    public Transform firePoint;
    public Transform pivot;
    public GameObject serverBulletPrefab;
    public GameObject clientBulletPrefab;
    public GameObject[] muzzleFlashPrefabs;
    public GameObject[] muzzleLightPrefabs;
    public GameObject bulletShellPrefab;
    public Transform shellEjectionPoint;
    public float muzzleFlashDuration = 0.1f;
    public float muzzleLightDuration = 0.08f;
    [Range(0f, 1f)] public float muzzleFlashChance = 0.8f;
    public float bulletSpeed = 20f;
    public float bulletDamage = 20f;
    public float fireRate = 6f;
    public bool holdToFire = true;
    public int bulletsPerShot = 1;
    public float spreadAngle = 15f;
    public HandAim handAim;
    public float recoilAmount = 1f;
    [SerializeField] private int maxActiveBulletsPerShooter = 32;
    public float screenShakeIntensity = 0.15f;
    public float screenShakeDuration = 0.1f;

    public static System.Action OnLocalShot;
    public static System.Action<float> OnLocalReloadStart;
    public static System.Action OnLocalReloadEnd;

    // Gun-specific sounds
    [Header("Gun Audio")]
    public AudioClip gunFireClip;
    public AudioClip reloadClip;
    [Range(0f, 1f)] public float gunFireVolume = 0.8f;
    [Range(0f, 1f)] public float reloadVolume = 0.6f;
    [Tooltip("Optional: Secondary/tail sound for impact punch (e.g., click, shell casing). Leave empty for single-sound shots.")]
    public AudioClip gunFireTailClip;
    [Range(0f, 1f)] public float gunFireTailVolume = 0.3f;
    [Tooltip("Delay in seconds before playing tail sound for layered impact effect")]
    [Range(0f, 0.05f)] public float gunFireTailDelay = 0.01f;

    public int maxAmmo = 30;
    public float reloadTime = 1.5f;
    private int currentAmmo;
    private bool isReloading = false;

    public int CurrentAmmo => currentAmmo;
    public bool IsReloading => isReloading;

    private float nextShootTime;
    private readonly Dictionary<ulong, float> serverNextShootTime = new Dictionary<ulong, float>();

    void ResolveHandAim()
    {
        if (handAim != null) return;

        handAim = GetComponent<HandAim>();
        if (handAim == null && pivot != null)
        {
            handAim = pivot.GetComponent<HandAim>();
        }
        if (handAim == null)
        {
            handAim = GetComponentInParent<HandAim>();
        }

        if (handAim != null && pivot != null)
        {
            handAim.pivot = this.pivot;
        }
    }

    void Awake()
    {
        ResolveHandAim();
    }

    void Start()
    {
        currentAmmo = maxAmmo;
        ResolveHandAim();
    }

    public int AddAmmo(int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        int previousAmmo = currentAmmo;
        currentAmmo = Mathf.Min(maxAmmo, currentAmmo + amount);
        return currentAmmo - previousAmmo;
    }

    public void RefillAmmo()
    {
        currentAmmo = maxAmmo;
    }

    void Update()
    {
        if (handAim == null) ResolveHandAim();

        if (!IsOwner) return;

        if (isReloading) return;

        // Auto-reload when ammo is 0, or manual reload when pressing R.
        if (currentAmmo <= 0 || (Input.GetKeyDown(KeyCode.R) && currentAmmo < maxAmmo))
        {
            StartCoroutine(ReloadRoutine());
            return;
        }

        bool wantsToShoot = holdToFire ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
        if (!wantsToShoot) return;

        if (Time.time >= nextShootTime)
        {
            Shoot();

            float safeFireRate = Mathf.Max(0.01f, fireRate);
            nextShootTime = Time.time + (1f / safeFireRate);
        }
    }

    System.Collections.IEnumerator ReloadRoutine()
    {
        isReloading = true;
        OnLocalReloadStart?.Invoke(reloadTime);

        // Play reload sound
        if (reloadClip != null)
        {
            PlayReloadSoundServerRpc();
        }

        yield return new WaitForSeconds(reloadTime);

        currentAmmo = maxAmmo;
        isReloading = false;
        OnLocalReloadEnd?.Invoke();
    }

    System.Collections.IEnumerator PlayGunSoundTail()
    {
        yield return new WaitForSeconds(gunFireTailDelay);
        if (gunFireTailClip != null && SoundManager.Instance != null)
        {
            Vector3 listenerPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            SoundManager.Instance.PlayGunSoundMultiplayer(gunFireTailClip, firePoint.position, gunFireTailVolume, isLocalPlayer: true, listenerPos);
        }
    }

    void Shoot()
    {
        currentAmmo--;
        OnLocalShot?.Invoke();

        Vector3 mouse = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouse.z = 0;

        // ใช้ pivot แทน firePoint
        Vector2 dir = (mouse - pivot.position).normalized;

        ResolveHandAim();
        float recoilJitter = 0f;
        if (handAim != null)
        {
            recoilJitter = Random.Range(-handAim.recoilRandomAngle, handAim.recoilRandomAngle);
            handAim.AddRecoilWithJitter(recoilAmount, recoilJitter);
        }

        // Spawn visual instantly for the shooter
        float safeJitterAngle = Mathf.Clamp(recoilJitter, -10f, 10f); // just as a safeguard 
        Vector2 finalDirVisual = Quaternion.Euler(0, 0, safeJitterAngle) * dir;

        float safeAngleVisual = Mathf.Atan2(finalDirVisual.y, finalDirVisual.x) * Mathf.Rad2Deg;

        if (muzzleFlashPrefabs != null && muzzleFlashPrefabs.Length > 0)
        {
            GameObject flashPrefab = muzzleFlashPrefabs[Random.Range(0, muzzleFlashPrefabs.Length)];
            GameObject flash = Instantiate(flashPrefab, firePoint.position, Quaternion.Euler(0, 0, safeAngleVisual));
            Destroy(flash, muzzleFlashDuration);
        }

        if (muzzleLightPrefabs != null && muzzleLightPrefabs.Length > 0)
        {
            GameObject lightPrefab = muzzleLightPrefabs[Random.Range(0, muzzleLightPrefabs.Length)];
            GameObject lightObj = Instantiate(lightPrefab, firePoint.position, Quaternion.Euler(0, 0, safeAngleVisual));
            Destroy(lightObj, muzzleLightDuration);
        }

        if (bulletShellPrefab != null)
        {
            Transform ejectPoint = shellEjectionPoint != null ? shellEjectionPoint : (pivot != null ? pivot : firePoint);
            Instantiate(bulletShellPrefab, ejectPoint.position, Quaternion.Euler(0, 0, safeAngleVisual));
        }

        // Play gun fire sound locally with multiplayer optimization
        if (gunFireClip != null && SoundManager.Instance != null)
        {
            Vector3 listenerPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            SoundManager.Instance.PlayGunSoundMultiplayer(gunFireClip, firePoint.position, gunFireVolume, isLocalPlayer: true, listenerPos);

            // Optional: Play tail sound for layered impact effect
            if (gunFireTailClip != null && gunFireTailDelay >= 0f)
            {
                StartCoroutine(PlayGunSoundTail());
            }
        }

        // Screen shake
        CameraFollow camFollow = Camera.main.GetComponent<CameraFollow>();
        if (camFollow != null)
        {
            camFollow.shakeIntensity = screenShakeIntensity;
            camFollow.shakeDuration = screenShakeDuration;
            camFollow.Shake();
        }

        int seed = Random.Range(int.MinValue, int.MaxValue);
        System.Random prng = new System.Random(seed);

        for (int i = 0; i < bulletsPerShot; i++)
        {
            float individualSpread = 0f;
            if (bulletsPerShot > 1)
            {
                individualSpread = (float)(prng.NextDouble() * spreadAngle - (spreadAngle / 2f));
            }

            if (clientBulletPrefab != null)
            {
                Vector2 bulletDir = Quaternion.Euler(0, 0, individualSpread) * finalDirVisual;
                GameObject visualBullet = Instantiate(clientBulletPrefab, firePoint.position, Quaternion.identity);
                ClientBullet cb = visualBullet.GetComponent<ClientBullet>();
                if (cb != null) cb.Initialize(bulletDir, bulletSpeed, bulletDamage, NetworkManager.Singleton.LocalClientId);
            }
        }

        ShootServerRpc(firePoint.position, dir, recoilJitter, seed);
    }

    bool CanServerShoot(ulong senderClientId)
    {
        float safeFireRate = Mathf.Max(0.01f, fireRate);
        float interval = 1f / safeFireRate;

        if (!serverNextShootTime.TryGetValue(senderClientId, out float allowedAt))
        {
            serverNextShootTime[senderClientId] = Time.time + interval;
            return true;
        }

        // Add 0.15s tolerance for network jitter so legitimate shots aren't dropped
        if (Time.time < allowedAt - 0.15f)
        {
            return false;
        }

        serverNextShootTime[senderClientId] = Time.time + interval;
        return true;
    }

    float GetLatencyCompensationSeconds(ulong senderClientId)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.NetworkConfig == null)
        {
            return 0f;
        }

        ulong rttMs = NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(senderClientId);
        float oneWaySeconds = (rttMs * 0.5f) / 1000f;

        // Keep compensation very small to avoid visible over-leading on clients.
        return Mathf.Clamp(oneWaySeconds, 0f, 0.03f);
    }

    [ServerRpc]
    void ShootServerRpc(Vector2 pos, Vector2 dir, float recoilJitter, int seed, ServerRpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (!CanServerShoot(senderClientId)) return;

        float maxJitter = handAim != null ? Mathf.Abs(handAim.recoilRandomAngle) : 0f;
        float safeRecoilJitter = Mathf.Clamp(recoilJitter, -maxJitter, maxJitter);

        // Notify other clients to play recoil and spawn visuals
        PlayShootEffectsClientRpc(pos, dir, recoilAmount, safeRecoilJitter, seed, senderClientId);

        // Play fire sound for other clients
        PlayFireSoundClientRpc(pos);

        if (serverBulletPrefab == null) return;

        Vector2 safeDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        Vector2 finalDir = Quaternion.Euler(0, 0, safeRecoilJitter) * safeDir;

        float latencyCompensation = GetLatencyCompensationSeconds(senderClientId);
        System.Random prng = new System.Random(seed);

        for (int i = 0; i < bulletsPerShot; i++)
        {
            float individualSpread = 0f;
            if (bulletsPerShot > 1)
            {
                individualSpread = (float)(prng.NextDouble() * spreadAngle - (spreadAngle / 2f));
            }

            Vector2 bulletDir = Quaternion.Euler(0, 0, individualSpread) * finalDir;
            Vector2 compensatedPos = pos + (bulletDir * bulletSpeed * latencyCompensation);
            float angle = Mathf.Atan2(bulletDir.y, bulletDir.x) * Mathf.Rad2Deg;

            GameObject bullet = Instantiate(serverBulletPrefab, compensatedPos, Quaternion.Euler(0f, 0f, angle));
            ServerBullet serverBullet = bullet.GetComponent<ServerBullet>();
            if (serverBullet != null)
            {
                serverBullet.Initialize(bulletDir, bulletSpeed, bulletDamage, senderClientId);
            }
        }
    }

    [ClientRpc]
    void PlayShootEffectsClientRpc(Vector2 pos, Vector2 dir, float amount, float recoilJitter, int seed, ulong shooterId)
    {
        // Owner already played recoil and spawned its own visual bullet instantly
        if (NetworkManager.Singleton.LocalClientId == shooterId) return;

        Vector2 safeDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        Vector2 finalDir = Quaternion.Euler(0, 0, recoilJitter) * safeDir;

        ResolveHandAim();
        if (handAim != null)
        {
            handAim.AddRecoilWithJitter(amount, recoilJitter);
        }

        Vector3 spawnPos = firePoint != null ? firePoint.position : (Vector3)pos;
        float safeAngleVisual = Mathf.Atan2(finalDir.y, finalDir.x) * Mathf.Rad2Deg;

        if (muzzleFlashPrefabs != null && muzzleFlashPrefabs.Length > 0)
        {
            GameObject flashPrefab = muzzleFlashPrefabs[Random.Range(0, muzzleFlashPrefabs.Length)];
            GameObject flash = Instantiate(flashPrefab, spawnPos, Quaternion.Euler(0, 0, safeAngleVisual));
            Destroy(flash, muzzleFlashDuration);
        }

        if (muzzleLightPrefabs != null && muzzleLightPrefabs.Length > 0)
        {
            GameObject lightPrefab = muzzleLightPrefabs[Random.Range(0, muzzleLightPrefabs.Length)];
            GameObject lightObj = Instantiate(lightPrefab, spawnPos, Quaternion.Euler(0, 0, safeAngleVisual));
            Destroy(lightObj, muzzleLightDuration);
        }

        if (bulletShellPrefab != null)
        {
            Transform ejectPoint = shellEjectionPoint != null ? shellEjectionPoint : (pivot != null ? pivot : firePoint);
            Vector3 finalEjectPos = ejectPoint != null ? ejectPoint.position : spawnPos;
            Instantiate(bulletShellPrefab, finalEjectPos, Quaternion.Euler(0, 0, safeAngleVisual));
        }

        if (clientBulletPrefab != null)
        {
            System.Random prng = new System.Random(seed);
            for (int i = 0; i < bulletsPerShot; i++)
            {
                float individualSpread = 0f;
                if (bulletsPerShot > 1)
                {
                    individualSpread = (float)(prng.NextDouble() * spreadAngle - (spreadAngle / 2f));
                }

                Vector2 bulletDir = Quaternion.Euler(0, 0, individualSpread) * finalDir;
                GameObject visualBullet = Instantiate(clientBulletPrefab, spawnPos, Quaternion.identity);
                ClientBullet cb = visualBullet.GetComponent<ClientBullet>();
                if (cb != null) cb.Initialize(bulletDir, bulletSpeed, bulletDamage, shooterId);
            }
        }
    }

    [ServerRpc]
    void PlayReloadSoundServerRpc()
    {
        // Broadcast reload sound to all clients
        PlayReloadSoundClientRpc();
    }

    [ClientRpc]
    void PlayReloadSoundClientRpc()
    {
        // Play reload sound for all clients
        if (reloadClip != null && SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayGunSoundFollowingTransform(reloadClip, transform, reloadVolume);
        }
    }

    [ServerRpc]
    void PlayFireSoundServerRpc(Vector3 gunPosition)
    {
        // Broadcast fire sound to all clients (except the shooter who already played it locally)
        PlayFireSoundClientRpc(gunPosition);
    }

    [ClientRpc]
    void PlayFireSoundClientRpc(Vector3 gunPosition)
    {
        // Only play if we're not the owner (owner already played it locally in Shoot())
        if (IsOwner) return;

        if (gunFireClip != null && SoundManager.Instance != null)
        {
            Vector3 listenerPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            SoundManager.Instance.PlayGunSoundMultiplayer(gunFireClip, gunPosition, gunFireVolume, isLocalPlayer: false, listenerPos);
        }
    }

}