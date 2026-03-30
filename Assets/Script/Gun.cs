using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class Gun : NetworkBehaviour
{
    public Transform firePoint;
    public Transform pivot;
    public GameObject bulletPrefab;
    public float bulletSpeed = 20f;
    public float fireRate = 6f;
    public bool holdToFire = true;
    public HandAim handAim;
    public float recoilAmount = 1f;

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
    }

    void Awake()
    {
        ResolveHandAim();
    }

    void Update()
    {
        if (!IsOwner) return;

        bool wantsToShoot = holdToFire ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
        if (!wantsToShoot) return;

        if (Time.time >= nextShootTime)
        {
            Shoot();

            float safeFireRate = Mathf.Max(0.01f, fireRate);
            nextShootTime = Time.time + (1f / safeFireRate);
        }
    }

    void Shoot()
    {
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

        if (Time.time < allowedAt)
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

        // Prevent overly large forward spawn offsets on temporary lag spikes.
        return Mathf.Clamp(oneWaySeconds, 0f, 0.15f);
    }

    [ServerRpc]
    void ShootServerRpc(Vector2 pos, Vector2 dir, float recoilJitter, ServerRpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (!CanServerShoot(senderClientId)) return;

        float maxJitter = handAim != null ? Mathf.Abs(handAim.recoilRandomAngle) : 0f;
        float safeRecoilJitter = Mathf.Clamp(recoilJitter, -maxJitter, maxJitter);
        PlayRecoilClientRpc(recoilAmount, safeRecoilJitter);

        if (bulletPrefab == null) return;

        Vector2 safeDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        float latencyCompensation = GetLatencyCompensationSeconds(senderClientId);
        Vector2 compensatedPos = pos + (safeDir * bulletSpeed * latencyCompensation);
        float angle = Mathf.Atan2(safeDir.y, safeDir.x) * Mathf.Rad2Deg;
        GameObject bullet = Instantiate(bulletPrefab, compensatedPos, Quaternion.Euler(0f, 0f, angle));

        Bullet bulletComponent = bullet.GetComponent<Bullet>();
        if (bulletComponent != null)
        {
            bulletComponent.SetServerDirection(safeDir);
        }

        NetworkObject netObj = bullet.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Destroy(bullet);
            return;
        }
        netObj.Spawn();

        Rigidbody2D rb = bullet.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = safeDir * bulletSpeed;
        }
    }

    [ClientRpc]
    void PlayRecoilClientRpc(float amount, float recoilJitter)
    {
        if (IsOwner) return;

        ResolveHandAim();
        if (handAim != null)
        {
            handAim.AddRecoilWithJitter(amount, recoilJitter);
        }
    }
}