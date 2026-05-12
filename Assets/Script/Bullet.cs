using UnityEngine;
using Unity.Netcode;

public class Bullet : NetworkBehaviour
{
    private Rigidbody2D rb;
    private Vector3 lastPosition;
    [SerializeField] private float maxLifetimeSeconds = 2f;
    [SerializeField] private float defaultDamage = 10f;
    private float despawnAtTime;
    private float damage;
    private ulong shooterClientId = ulong.MaxValue;
    private NetworkVariable<Vector2> netDirection = new NetworkVariable<Vector2>(
        Vector2.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<float> netSpeed = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<double> netServerSpawnTime = new NetworkVariable<double>(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<Vector2> netSpawnPosition = new NetworkVariable<Vector2>(
        Vector2.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        lastPosition = transform.position;
    }

    public override void OnNetworkSpawn()
    {
        lastPosition = transform.position;
        despawnAtTime = Time.time + Mathf.Max(0.1f, maxLifetimeSeconds);
        damage = Mathf.Max(0f, defaultDamage);

        if (!IsServer)
        {
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.simulated = false;
            }

            ApplyClientTrajectory(true);
        }
    }

    public override void OnNetworkDespawn()
    {
    }

    public void ConfigureServerDamage(float bulletDamage, ulong shooterId)
    {
        if (!IsServer) return;

        damage = Mathf.Max(0f, bulletDamage);
        shooterClientId = shooterId;
    }

    public void SetServerDirection(Vector2 dir)
    {
        if (!IsServer) return;

        if (dir.sqrMagnitude > 0.0001f)
        {
            netDirection.Value = dir.normalized;
        }

        netServerSpawnTime.Value = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.ServerTime.Time
            : 0d;
    }

    public void SetServerSpawnPosition(Vector2 spawnPosition)
    {
        if (!IsServer) return;

        netSpawnPosition.Value = spawnPosition;
    }

    public void SetServerSpeed(float speed)
    {
        if (!IsServer) return;

        netSpeed.Value = Mathf.Max(0f, speed);
    }

    void Update()
    {
        if (IsServer && Time.time >= despawnAtTime)
        {
            DespawnBullet();
            return;
        }

        if (!IsServer)
        {
            ApplyClientTrajectory(false);
            lastPosition = transform.position;
            return;
        }

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector2 moveDeltaDir = ((Vector2)transform.position - (Vector2)lastPosition) / dt;
        Vector2 dir = Vector2.zero;

        if (IsServer)
        {
            // Keep server/host behavior based on authoritative physics velocity.
            if (rb != null && rb.linearVelocity.sqrMagnitude > 0.0001f)
            {
                dir = rb.linearVelocity;
            }
            else if (moveDeltaDir.sqrMagnitude > 0.0001f)
            {
                dir = moveDeltaDir;
            }
        }
        else
        {
            // Clients may not have reliable Rigidbody velocity, so prefer replicated shot direction.
            if (netDirection.Value.sqrMagnitude > 0.0001f)
            {
                dir = netDirection.Value;
            }
            else if (moveDeltaDir.sqrMagnitude > 0.0001f)
            {
                dir = moveDeltaDir;
            }
        }

        if (dir.sqrMagnitude > 0.0001f)
        {
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }

        lastPosition = transform.position;
    }

    void OnTriggerEnter2D(Collider2D col)
    {
        if (!IsServer) return;

        if (col.CompareTag("Player"))
        {
            // Ignore player collisions entirely.
            return;
        }

        Character character = col.GetComponentInParent<Character>();
        if (character != null)
        {
            if (character.CompareTag("Player"))
            {
                // Ignore player collisions entirely.
                return;
            }

            // Ignore the shooter's own collider so bullets can continue flying.
            if (character.OwnerClientId == shooterClientId) return;

            PlayBulletHitSoundClientRpc(transform.position);
            character.TakeDamage(damage);

            DespawnBullet();
            return;
        }

        PlayBulletHitSoundClientRpc(transform.position);
        DespawnBullet();
    }

    [ClientRpc]
    private void PlayBulletHitSoundClientRpc(Vector3 hitPosition)
    {
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySound(SoundType.BulletHit, hitPosition);
        }
    }

    private void DespawnBullet()
    {
        NetworkObject netObj = GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
        {
            netObj.Despawn(true);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void ApplyClientTrajectory(bool snapImmediately)
    {
        if (NetworkManager.Singleton == null) return;

        Vector2 dir = netDirection.Value.sqrMagnitude > 0.0001f ? netDirection.Value.normalized : Vector2.zero;
        float speed = Mathf.Max(0f, netSpeed.Value);
        if (dir == Vector2.zero || speed <= 0f || netServerSpawnTime.Value <= 0d)
        {
            return;
        }

        double elapsed = NetworkManager.Singleton.ServerTime.Time - netServerSpawnTime.Value;
        float elapsedSeconds = Mathf.Max(0f, (float)elapsed);
        Vector2 targetPosition = netSpawnPosition.Value + (dir * speed * elapsedSeconds);

        transform.position = targetPosition;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }
    }

}