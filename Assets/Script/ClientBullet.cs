using UnityEngine;

public class ClientBullet : MonoBehaviour
{
    [SerializeField] private float maxLifetimeSeconds = 2f;
    [SerializeField] private GameObject hitEffectPrefab;

    private float despawnAtTime;
    private Vector2 direction;
    private float speed;

    private ulong shooterId;

    public void Initialize(Vector2 dir, float moveSpeed, ulong shooterClientId)
    {
        direction = dir.normalized;
        speed = moveSpeed;
        shooterId = shooterClientId;
        despawnAtTime = Time.time + maxLifetimeSeconds;

        if (direction.sqrMagnitude > 0.001f)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }
    }

    void Update()
    {
        if (Time.time >= despawnAtTime)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 moveDelta = (Vector3)(direction * speed * Time.deltaTime);

        // Raycast to detect walls or enemies so the visual bullet stops
        RaycastHit2D[] hits = Physics2D.RaycastAll(transform.position, direction, moveDelta.magnitude);
        foreach (var hit in hits)
        {
            if (hit.collider != null)
            {
                if (hit.collider.CompareTag("Player")) continue;
                if (hit.collider.GetComponent<BulletShell>() != null) continue;

                Character charHit = hit.collider.GetComponentInParent<Character>();
                if (charHit != null)
                {
                    if (charHit.CompareTag("Player")) continue;

                    // Found an enemy
                    charHit.PlayLocalDamageFlash();
                    SpawnHitEffect(hit.point, hit.normal, true);
                    Destroy(gameObject);
                    return;
                }

                if (hit.collider.isTrigger) continue; // Ignore triggers like aggro ranges

                // Hit a wall
                SpawnHitEffect(hit.point, hit.normal, false);
                Destroy(gameObject);
                return;
            }
        }

        transform.position += moveDelta;
    }

    private void SpawnHitEffect(Vector2 position, Vector2 normal, bool isEnemy)
    {
        if (hitEffectPrefab != null)
        {
            float angle = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg;
            GameObject effect = Instantiate(hitEffectPrefab, position, Quaternion.Euler(0, 0, angle));

            if (isEnemy)
            {
                SpriteRenderer sr = effect.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = Color.red;

                ParticleSystem ps = effect.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    var main = ps.main;
                    main.startColor = Color.red;
                }
            }

            Destroy(effect, 1f); // Adjust duration if needed
        }
    }
}