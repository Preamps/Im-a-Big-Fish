using UnityEngine;

public class ClientBullet : MonoBehaviour
{
    [SerializeField] private float maxLifetimeSeconds = 2f;

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
                    Destroy(gameObject);
                    return;
                }

                if (hit.collider.isTrigger) continue; // Ignore triggers like aggro ranges

                // Hit a wall
                Destroy(gameObject);
                return;
            }
        }

        transform.position += moveDelta;
    }
}