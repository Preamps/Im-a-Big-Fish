using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class Enemy : Character
{
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float stopDistance = 0.2f;
    [SerializeField] private float retargetInterval = 0.5f;
    [SerializeField] private float acceleration = 25f;
    [SerializeField] private float deceleration = 30f;
    [SerializeField] private float networkSmoothTime = 0.06f;
    [SerializeField] private float networkSendRate = 20f;
    [SerializeField] private float networkPositionThreshold = 0.02f;
    [SerializeField] private float networkHeartbeatSeconds = 0.2f;
    [SerializeField] private float walkAnimThreshold = 0.05f;
    [SerializeField] private string walkBoolParam = "IsWalking";
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private bool spriteFacesRight = true;
    [SerializeField] private float touchDamage = 10f;
    [SerializeField] private float touchDamageInterval = 0.5f;
    [SerializeField] private int killReward = 10;

    public float damage;
    protected Transform targetPlayer;
    private Rigidbody2D rb;
    private Collider2D enemyCollider;
    private Vector2 clientSmoothVelocity;
    private int walkBoolParamHash;
    private float nextTouchDamageTime;
    private float nextNetworkSyncTime;
    private float nextNetworkHeartbeatTime;
    private Vector2 lastSentPosition;
    private bool lastSentFlipX;
    private bool lastSentIsWalking;
    private bool hasSentState;

    private float nextRetargetTime;
    private NetworkVariable<Vector2> netPosition = new NetworkVariable<Vector2>(
        Vector2.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<bool> netFlipX = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private NetworkVariable<bool> netIsWalking = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    void Awake()
    {
        Speed = Mathf.Max(0f, moveSpeed);
        rb = GetComponent<Rigidbody2D>();
        enemyCollider = GetComponent<Collider2D>();
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        walkBoolParamHash = Animator.StringToHash(walkBoolParam);

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Ignore collisions with all other currently spawned enemies
        Enemy[] allEnemies = Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None);
        foreach (Enemy e in allEnemies)
        {
            if (e == this) continue;
            if (e.enemyCollider == null || this.enemyCollider == null) continue;

            Physics2D.IgnoreCollision(this.enemyCollider, e.enemyCollider, true);
        }

        // Ignore collisions with all currently spawned players
        Player[] allPlayers = Object.FindObjectsByType<Player>(FindObjectsSortMode.None);
        foreach (Player p in allPlayers)
        {
            Collider2D[] pCols = p.GetComponentsInChildren<Collider2D>(true);
            foreach (var pCol in pCols)
            {
                if (pCol != null && this.enemyCollider != null && pCol.gameObject.activeInHierarchy && this.gameObject.activeInHierarchy)
                {
                    Physics2D.IgnoreCollision(this.enemyCollider, pCol, true);
                }
            }
        }

        if (IsServer)
        {
            netPosition.Value = rb.position;
            netFlipX.Value = spriteRenderer != null && spriteRenderer.flipX;
            netIsWalking.Value = Mathf.Abs(rb.linearVelocity.x) > walkAnimThreshold;
            lastSentPosition = netPosition.Value;
            lastSentFlipX = netFlipX.Value;
            lastSentIsWalking = netIsWalking.Value;
            hasSentState = true;
        }
    }

    void Update()
    {
        if (IsServer) return;

        Vector2 syncedPos = netPosition.Value;
        rb.position = Vector2.SmoothDamp(
            rb.position,
            syncedPos,
            ref clientSmoothVelocity,
            Mathf.Max(0.01f, networkSmoothTime)
        );

        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = netFlipX.Value;
        }

        if (animator != null)
        {
            animator.SetBool(walkBoolParamHash, netIsWalking.Value);
        }
    }

    void FixedUpdate()
    {
        if (!IsServer) return;

        Move();
        TryDamagePlayerOverlap();
        bool isWalking = Mathf.Abs(rb.linearVelocity.x) > walkAnimThreshold;

        if (animator != null)
        {
            animator.SetBool(walkBoolParamHash, isWalking);
        }

        float interval = 1f / Mathf.Max(1f, networkSendRate);
        if (Time.time < nextNetworkSyncTime)
        {
            return;
        }

        nextNetworkSyncTime = Time.time + interval;
        Vector2 currentPosition = rb.position;
        bool currentFlipX = spriteRenderer != null && spriteRenderer.flipX;
        bool isHeartbeatDue = Time.time >= nextNetworkHeartbeatTime;

        bool movedEnough = !hasSentState || Vector2.Distance(currentPosition, lastSentPosition) >= Mathf.Max(0.001f, networkPositionThreshold);
        bool stateChanged = !hasSentState || currentFlipX != lastSentFlipX || isWalking != lastSentIsWalking;

        if (!movedEnough && !stateChanged && !isHeartbeatDue)
        {
            return;
        }

        netPosition.Value = currentPosition;
        netFlipX.Value = currentFlipX;
        netIsWalking.Value = isWalking;

        lastSentPosition = currentPosition;
        lastSentFlipX = currentFlipX;
        lastSentIsWalking = isWalking;
        hasSentState = true;
        nextNetworkHeartbeatTime = Time.time + Mathf.Max(0.05f, networkHeartbeatSeconds);
    }

    protected override void Die()
    {
        if (IsServer && lastDamagerId != ulong.MaxValue)
        {
            // Give money to the player who dealt the final damage
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(lastDamagerId, out var client))
            {
                var player = client.PlayerObject?.GetComponent<Player>();
                if (player != null)
                {
                    player.Money.Value += killReward;
                    player.KillCount.Value += 1;
                }
            }
        }

        base.Die();
    }

    protected void FindTarget()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        Transform closestTarget = null;
        float bestDistanceSqr = float.MaxValue;
        Vector3 enemyPos = transform.position;

        for (int i = 0; i < players.Length; i++)
        {
            GameObject playerObj = players[i];
            if (playerObj == null || !playerObj.activeInHierarchy)
            {
                continue;
            }

            Player p = playerObj.GetComponentInParent<Player>();
            if (p != null && (p.IsDead.Value || p.IsDown.Value))
            {
                continue; // Ignore dead or downed players
            }

            Vector3 delta = playerObj.transform.position - enemyPos;
            float distanceSqr = delta.sqrMagnitude;
            if (distanceSqr < bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                closestTarget = playerObj.transform;
            }
        }

        targetPlayer = closestTarget;
        nextRetargetTime = Time.time + Mathf.Max(0.1f, retargetInterval);
    }

    public override void Move()
    {
        if (targetPlayer == null || Time.time >= nextRetargetTime)
        {
            FindTarget();
        }

        if (targetPlayer == null)
        {
            float newVelX = Mathf.MoveTowards(
                rb.linearVelocity.x,
                0f,
                Mathf.Max(0f, deceleration) * Time.fixedDeltaTime
            );
            rb.linearVelocity = new Vector2(newVelX, rb.linearVelocity.y);
            return;
        }

        Vector2 currentPos = rb.position;
        Vector2 targetPos = targetPlayer.position;
        float horizontalDelta = targetPos.x - currentPos.x;
        float distance = Mathf.Abs(horizontalDelta);

        if (distance <= stopDistance)
        {
            float newVelX = Mathf.MoveTowards(
                rb.linearVelocity.x,
                0f,
                Mathf.Max(0f, deceleration) * Time.fixedDeltaTime
            );
            rb.linearVelocity = new Vector2(newVelX, rb.linearVelocity.y);
            return;
        }

        float moveX = Mathf.Sign(horizontalDelta) * Speed;
        float nextVelX = Mathf.MoveTowards(
            rb.linearVelocity.x,
            moveX,
            Mathf.Max(0f, acceleration) * Time.fixedDeltaTime
        );
        rb.linearVelocity = new Vector2(nextVelX, rb.linearVelocity.y);

        if (Mathf.Abs(moveX) > 0.01f)
        {
            bool movingLeft = moveX < 0f;
            bool shouldFlip = spriteFacesRight ? movingLeft : !movingLeft;
            if (spriteRenderer != null)
            {
                spriteRenderer.flipX = shouldFlip;
            }
        }
    }

    private void TryDamagePlayerOverlap()
    {
        if (Time.time < nextTouchDamageTime || enemyCollider == null) return;

        Collider2D[] results = Physics2D.OverlapBoxAll(enemyCollider.bounds.center, enemyCollider.bounds.size + new Vector3(0.2f, 0.2f, 0f), 0f);
        foreach (Collider2D other in results)
        {
            if (other == null) continue;
            Player player = other.GetComponentInParent<Player>();
            if (player != null && player.CompareTag("Player") && !player.IsDead.Value && !player.IsDown.Value)
            {
                player.TakeDamage(touchDamage);
                nextTouchDamageTime = Time.time + Mathf.Max(0.05f, touchDamageInterval);
                return;
            }
        }
    }
}
