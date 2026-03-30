using UnityEngine;
using Unity.Netcode;

public class Bullet : NetworkBehaviour
{
    private Rigidbody2D rb;
    private Vector3 lastPosition;
    [SerializeField] private float maxLifetimeSeconds = 5f;
    private float despawnAtTime;
    private NetworkVariable<Vector2> netDirection = new NetworkVariable<Vector2>(
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
    }

    public void SetServerDirection(Vector2 dir)
    {
        if (!IsServer) return;

        if (dir.sqrMagnitude > 0.0001f)
        {
            netDirection.Value = dir.normalized;
        }
    }

    void Update()
    {
        if (IsServer && Time.time >= despawnAtTime)
        {
            DespawnBullet();
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
        if (col.CompareTag("Player")) return;

        DespawnBullet();
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
}