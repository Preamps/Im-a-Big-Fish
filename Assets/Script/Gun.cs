using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class Gun : NetworkBehaviour
{
    public Transform firePoint;
    public Transform pivot;
    public GameObject bulletPrefab;
    public float bulletSpeed = 20f;
    public float bulletDamage = 20f;
    public float fireRate = 6f;
    public bool holdToFire = true;
    public HandAim handAim;
    public float recoilAmount = 1f;
    [SerializeField] private int maxActiveBulletsPerShooter = 32;

    private float nextShootTime;
    private readonly Dictionary<ulong, float> serverNextShootTime = new Dictionary<ulong, float>();
    private readonly Dictionary<ulong, Queue<NetworkObject>> serverActiveBullets = new Dictionary<ulong, Queue<NetworkObject>>();

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

        // Keep compensation very small to avoid visible over-leading on clients.
        return Mathf.Clamp(oneWaySeconds, 0f, 0.03f);
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
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
            bulletComponent.SetServerSpawnPosition(compensatedPos);
            bulletComponent.SetServerDirection(safeDir);
            bulletComponent.SetServerSpeed(bulletSpeed);
            bulletComponent.ConfigureServerDamage(bulletDamage, senderClientId);
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

        RegisterAndTrimShooterBullets(senderClientId, netObj);
    }

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    void PlayRecoilClientRpc(float amount, float recoilJitter)
    {
        if (IsOwner) return;

        ResolveHandAim();
        if (handAim != null)
        {
            handAim.AddRecoilWithJitter(amount, recoilJitter);
        }
    }

    private void RegisterAndTrimShooterBullets(ulong shooterClientId, NetworkObject bulletObject)
    {
        int cap = Mathf.Max(1, maxActiveBulletsPerShooter);

        if (!serverActiveBullets.TryGetValue(shooterClientId, out Queue<NetworkObject> queue))
        {
            queue = new Queue<NetworkObject>(cap);
            serverActiveBullets[shooterClientId] = queue;
        }

        // Drop stale references first.
        int staleGuard = queue.Count;
        for (int i = 0; i < staleGuard; i++)
        {
            NetworkObject head = queue.Peek();
            if (head != null && head.IsSpawned)
            {
                break;
            }

            queue.Dequeue();
        }

        queue.Enqueue(bulletObject);

        while (queue.Count > cap)
        {
            NetworkObject oldest = queue.Dequeue();
            if (oldest != null && oldest.IsSpawned)
            {
                oldest.Despawn(true);
            }
        }
    }
}