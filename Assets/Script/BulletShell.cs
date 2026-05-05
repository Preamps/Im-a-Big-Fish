using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class BulletShell : MonoBehaviour
{
    public float minForce = 2f;
    public float maxForce = 4f;
    public float lifeTime = 3f;
    public float rotationSpeed = 360f;

    private Rigidbody2D rb;
    private Collider2D myCol;

    public static List<Collider2D> activeShells = new List<Collider2D>();

    void Awake()
    {
        myCol = GetComponent<Collider2D>();
        if (myCol != null)
        {
            // Ignore collisions with other shells dynamically to prevent lag/jitter
            activeShells.RemoveAll(s => s == null);
            foreach (var shellCol in activeShells)
            {
                if (shellCol != null && shellCol.gameObject.activeInHierarchy)
                {
                    Physics2D.IgnoreCollision(myCol, shellCol);
                }
            }
            activeShells.Add(myCol);
        }
    }

    void OnDestroy()
    {
        if (myCol != null)
        {
            activeShells.Remove(myCol);
        }
    }

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();

        if (myCol != null)
        {
            // Ignore collisions with all characters and networked objects (players, enemies, items)
            Character[] characters = FindObjectsByType<Character>(FindObjectsSortMode.None);
            foreach (var chara in characters)
            {
                Collider2D[] cols = chara.GetComponentsInChildren<Collider2D>(true);
                foreach (var c in cols)
                {
                    if (c != null && c.gameObject.activeInHierarchy)
                    {
                        Physics2D.IgnoreCollision(myCol, c);
                    }
                }
            }

            // Also ignore any trigger colliders in the scene
            Collider2D[] allTriggers = FindObjectsByType<Collider2D>(FindObjectsSortMode.None);
            foreach (var c in allTriggers)
            {
                if (c != null && c.isTrigger && c != myCol)
                {
                    Physics2D.IgnoreCollision(myCol, c);
                }
            }
        }

        if (rb != null)
        {
            float force = Random.Range(minForce, maxForce);
            // eject up and slightly backwards depending on facing direction
            float dirX = Mathf.Sign(transform.right.x) * -1f + Random.Range(-0.5f, 0.5f);
            Vector2 ejectDir = new Vector2(dirX, 1f).normalized;
            rb.AddForce(ejectDir * force, ForceMode2D.Impulse);
            rb.angularVelocity = Random.Range(-rotationSpeed, rotationSpeed);
        }
        Destroy(gameObject, lifeTime);
    }
}
