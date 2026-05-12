using Unity.Netcode;
using UnityEngine;

public class ServerBullet : MonoBehaviour
{
    private Rigidbody2D rb;
    [SerializeField] private float maxLifetimeSeconds = 2f;
    [SerializeField] private float defaultDamage = 10f;

    private float despawnAtTime;
    private float damage;
    private ulong shooterClientId;

    public void Initialize(Vector2 direction, float speed, float bulletDamage, ulong shooterId)
    {
        rb = GetComponent<Rigidbody2D>();
        damage = bulletDamage;
        shooterClientId = shooterId;
        despawnAtTime = Time.time + maxLifetimeSeconds;

        if (direction.sqrMagnitude > 0.001f)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        if (rb != null)
        {
            rb.linearVelocity = direction * speed;
        }

        Collider2D myCol = GetComponent<Collider2D>();
        if (myCol != null)
        {
            // Ignore collisions with all active bullet shells
            BulletShell.activeShells.RemoveAll(s => s == null);
            foreach (var shellCol in BulletShell.activeShells)
            {
                if (shellCol != null && shellCol.gameObject.activeInHierarchy)
                {
                    Physics2D.IgnoreCollision(myCol, shellCol);
                }
            }
        }

        // Hide server bullet so host doesn't see two bullets (client bullet + server bullet)
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.enabled = false;
    }

    void Update()
    {
        if (Time.time >= despawnAtTime)
        {
            Destroy(gameObject);
        }
    }

    // Support both Trigger and Physics collisions
    void OnTriggerEnter2D(Collider2D col)
    {
        HandleHit(col.gameObject, col);
    }

    void OnCollisionEnter2D(Collision2D col)
    {
        HandleHit(col.gameObject, col.collider);
    }

    private void HandleHit(GameObject hitObj, Collider2D col)
    {
        if (hitObj.CompareTag("Player"))
        {
            return;
        }

        if (hitObj.GetComponent<BulletShell>() != null)
        {
            return;
        }

        Character character = hitObj.GetComponentInParent<Character>();
        if (character != null)
        {
            if (character.CompareTag("Player")) return;

            // Client bullets handle damage now using NotifyHitServerRpc
            // But we still destroy the server bullet so it doesn't pass through
            Destroy(gameObject);
            return;
        }

        // If it's just a random trigger zone (like aggro range), pass through it
        if (col.isTrigger)
        {
            return;
        }

        // Hit a solid wall or obstacle
        // Client bullets already play hit sounds locally, so server doesn't need to do it anymore
        // PlayWallHitSoundClientRpc(transform.position);
        Destroy(gameObject);
    }

    [ClientRpc]
    private void PlayWallHitSoundClientRpc(Vector3 position)
    {
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySound(SoundType.BulletHitWall, position);
        }
    }
}