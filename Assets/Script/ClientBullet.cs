using UnityEngine;

public class ClientBullet : MonoBehaviour
{
    [SerializeField] private float maxLifetimeSeconds = 2f;
    [SerializeField] private GameObject hitEffectPrefab;

    private float despawnAtTime;
    private Vector2 direction;
    private float speed;
    private float damage;

    private ulong shooterId;

    public void Initialize(Vector2 dir, float moveSpeed, float bulletDamage, ulong shooterClientId)
    {
        direction = dir.normalized;
        speed = moveSpeed;
        damage = bulletDamage;
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
                    if (charHit.IsDead.Value) continue;

                    // Found an enemy
                    charHit.PlayLocalDamageFlash();

                    // Client authoritative hit - ONLY the shooter sends the damage
                    if (Unity.Netcode.NetworkManager.Singleton != null &&
                        Unity.Netcode.NetworkManager.Singleton.LocalClientId == shooterId &&
                        charHit.NetworkObject != null && charHit.NetworkObject.IsSpawned)
                    {
                        charHit.NotifyHitServerRpc(damage, shooterId);

                        // Client prediction: use a local health tracker so multiple bullets fired quickly 
                        // correctly predict death even before the server responds!
                        charHit.LocalPredictedHealth -= damage;
                        if (charHit.LocalPredictedHealth <= 0)
                        {
                            if (SoundManager.Instance != null && charHit is Enemy)
                            {
                                SoundManager.Instance.PlaySound(SoundType.EnemyDeath, charHit.transform.position);
                            }
                        }
                    }

                    SpawnHitEffect(hit.point, hit.normal, true);
                    Destroy(gameObject);
                    return;
                }

                if (hit.collider.isTrigger) continue; // Ignore triggers like aggro ranges

                // Hit a wall
                if (SoundManager.Instance != null)
                {
                    SoundManager.Instance.PlaySound(SoundType.BulletHitWall, hit.point);
                }
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