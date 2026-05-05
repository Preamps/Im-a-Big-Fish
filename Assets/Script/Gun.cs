using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class Gun : NetworkBehaviour
{
    public Transform firePoint;
    public Transform pivot;
    public GameObject serverBulletPrefab;
    public GameObject clientBulletPrefab;
    public GameObject muzzleFlashPrefab;
    public GameObject bulletShellPrefab;
    public Transform shellEjectionPoint;
    public float muzzleFlashDuration = 0.1f;
    public float bulletSpeed = 20f;
    public float bulletDamage = 20f;
    public float fireRate = 6f;
    public bool holdToFire = true;
    public HandAim handAim;
    public float recoilAmount = 1f;
    [SerializeField] private int maxActiveBulletsPerShooter = 32;

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
        // Optional: play reload sound or animation here

        yield return new WaitForSeconds(reloadTime);

        currentAmmo = maxAmmo;
        isReloading = false;
    }

    void Shoot()
    {
        currentAmmo--;

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

        if (muzzleFlashPrefab != null)
        {
            GameObject flash = Instantiate(muzzleFlashPrefab, firePoint.position, Quaternion.Euler(0, 0, safeAngleVisual));
            Destroy(flash, muzzleFlashDuration);
        }

        if (clientBulletPrefab != null)
        {
            GameObject visualBullet = Instantiate(clientBulletPrefab, firePoint.position, Quaternion.identity);
            ClientBullet cb = visualBullet.GetComponent<ClientBullet>();
            if (cb != null) cb.Initialize(finalDirVisual, bulletSpeed, NetworkManager.Singleton.LocalClientId);
        }

        if (bulletShellPrefab != null)
        {
            Transform ejectPoint = shellEjectionPoint != null ? shellEjectionPoint : (pivot != null ? pivot : firePoint);
            Instantiate(bulletShellPrefab, ejectPoint.position, Quaternion.Euler(0, 0, safeAngleVisual));
        }

        ShootServerRpc(firePoint.position, dir, recoilJitter);
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
    void ShootServerRpc(Vector2 pos, Vector2 dir, float recoilJitter, ServerRpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (!CanServerShoot(senderClientId)) return;

        float maxJitter = handAim != null ? Mathf.Abs(handAim.recoilRandomAngle) : 0f;
        float safeRecoilJitter = Mathf.Clamp(recoilJitter, -maxJitter, maxJitter);

        // Notify other clients to play recoil and spawn visuals
        PlayShootEffectsClientRpc(pos, dir, recoilAmount, safeRecoilJitter, senderClientId);

        if (serverBulletPrefab == null) return;

        Vector2 safeDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        Vector2 finalDir = Quaternion.Euler(0, 0, safeRecoilJitter) * safeDir;

        float latencyCompensation = GetLatencyCompensationSeconds(senderClientId);
        Vector2 compensatedPos = pos + (finalDir * bulletSpeed * latencyCompensation);
        float angle = Mathf.Atan2(finalDir.y, finalDir.x) * Mathf.Rad2Deg;

        GameObject bullet = Instantiate(serverBulletPrefab, compensatedPos, Quaternion.Euler(0f, 0f, angle));

        ServerBullet serverBullet = bullet.GetComponent<ServerBullet>();
        if (serverBullet != null)
        {
            serverBullet.Initialize(finalDir, bulletSpeed, bulletDamage, senderClientId);
        }
    }

    [ClientRpc]
    void PlayShootEffectsClientRpc(Vector2 pos, Vector2 dir, float amount, float recoilJitter, ulong shooterId)
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

        if (muzzleFlashPrefab != null)
        {
            GameObject flash = Instantiate(muzzleFlashPrefab, spawnPos, Quaternion.Euler(0, 0, safeAngleVisual));
            Destroy(flash, muzzleFlashDuration);
        }

        if (clientBulletPrefab != null)
        {
            GameObject visualBullet = Instantiate(clientBulletPrefab, spawnPos, Quaternion.identity);
            ClientBullet cb = visualBullet.GetComponent<ClientBullet>();
            if (cb != null) cb.Initialize(finalDir, bulletSpeed, shooterId);
        }

        if (bulletShellPrefab != null)
        {
            Transform ejectPoint = shellEjectionPoint != null ? shellEjectionPoint : (pivot != null ? pivot : firePoint);
            Vector3 finalEjectPos = ejectPoint != null ? ejectPoint.position : spawnPos;
            Instantiate(bulletShellPrefab, finalEjectPos, Quaternion.Euler(0, 0, safeAngleVisual));
        }
    }


}