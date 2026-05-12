using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class Enemy : Character
{
    protected override SoundType DeathSoundType => SoundType.EnemyDeath;

    private static readonly RaycastHit2D[] obstacleHitBuffer = new RaycastHit2D[8];
    private static readonly RaycastHit2D[] groundHitBuffer = new RaycastHit2D[8];
    private static readonly Collider2D[] overlapHitBuffer = new Collider2D[8];

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
    [SerializeField] private float jumpForce = 11f;
    [SerializeField] private float jumpCooldown = 0.75f;
    [SerializeField] private float obstacleCheckDistance = 0.25f;
    [SerializeField] private float obstacleCastInset = 0.03f;
    [SerializeField] private float obstacleSenseInterval = 0.1f;

    public float damage;
    protected Transform targetPlayer;
    private Rigidbody2D rb;
    private Collider2D enemyCollider;
    private PhysicsMaterial2D noFrictionMaterial;
    private Vector2 clientSmoothVelocity;
    private Vector2 clientInterpolatedPosition;
    private float lastNetworkUpdateTime;
    private int walkBoolParamHash;
    private float nextTouchDamageTime;
    private float nextNetworkSyncTime;
    private float nextNetworkHeartbeatTime;
    private Vector2 lastSentPosition;
    private bool lastSentFlipX;
    private bool lastSentIsWalking;
    private bool hasSentState;

    private float nextRetargetTime;
    private float nextObstacleSenseTime;
    private float nextJumpTime;
    private bool cachedObstacleAhead;
    private bool hasCachedObstacleAhead;
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

    public void ApplyWaveHealthBonus(int waveNumber, float healthIncreasePerWave)
    {
        int safeWaveNumber = Mathf.Max(1, waveNumber);
        float safeBonus = Mathf.Max(0f, healthIncreasePerWave);
        float bonusHealth = Mathf.Max(0f, safeWaveNumber - 1) * safeBonus;
        SetMaxHealth(MaxHealth + bonusHealth);
    }

    void Awake()
    {
        Speed = Mathf.Max(0f, moveSpeed);
        rb = GetComponent<Rigidbody2D>();
        enemyCollider = GetComponent<Collider2D>();
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        walkBoolParamHash = Animator.StringToHash(walkBoolParam);

        noFrictionMaterial = new PhysicsMaterial2D("EnemyNoFriction")
        {
            friction = 0f,
            bounciness = 0f
        };

        if (enemyCollider != null)
        {
            enemyCollider.sharedMaterial = noFrictionMaterial;
        }

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

        if (rb != null)
        {
            if (IsServer)
            {
                rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            }
            else
            {
                rb.bodyType = RigidbodyType2D.Kinematic; // Disable physics simulation on client
                rb.interpolation = RigidbodyInterpolation2D.None;
            }
        }

        float senseInterval = Mathf.Max(0.02f, obstacleSenseInterval);
        nextObstacleSenseTime = Time.time + (Mathf.Abs(GetInstanceID()) % 100) / 100f * senseInterval;

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
        else
        {
            // Client: Initialize interpolation from current network position
            clientInterpolatedPosition = netPosition.Value;
            lastNetworkUpdateTime = Time.time;
        }
    }

    void Update()
    {
        if (IsServer) return;

        float distance = Vector2.Distance(clientInterpolatedPosition, netPosition.Value);

        if (distance > 2f)
        {
            // Snap immediately if totally desynced (e.g. knocked back fast or spawned)
            clientInterpolatedPosition = netPosition.Value;
        }
        else
        {
            // Perfect hybrid: Move at least the base speed if walking to prevent "floaty Lerp crawl",
            // plus rubber-band catchup speed based on how far behind the network tick we are.
            float baseSpeed = netIsWalking.Value ? Speed : 0.1f;
            float catchupSpeed = baseSpeed + (distance * 12f);

            clientInterpolatedPosition = Vector2.MoveTowards(
                clientInterpolatedPosition,
                netPosition.Value,
                catchupSpeed * Time.deltaTime
            );
        }

        transform.position = clientInterpolatedPosition;

        // Client: Update visuals based on network state
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

        // Skip physics/AI if dead (prevents server from moving "dead but not despawned" zombies)
        if (Health.Value <= 0 || IsDead.Value) return;

        // Server: AI and physics logic only
        Move();
        TryDamagePlayerOverlap();
        bool isWalking = Mathf.Abs(rb.linearVelocity.x) > walkAnimThreshold;

        // Server: Network synchronization
        Vector2 currentPosition = rb.position;
        bool currentFlipX = spriteRenderer != null && spriteRenderer.flipX;

        // Remove threshold check to ensure continuous tracking, but only sync if position actually changed
        bool movedEnough = !hasSentState || Vector2.Distance(currentPosition, lastSentPosition) > 0.001f;
        bool stateChanged = !hasSentState || currentFlipX != lastSentFlipX || isWalking != lastSentIsWalking;
        bool isHeartbeatDue = Time.time >= nextNetworkHeartbeatTime;

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

                    BroadcastMoneyPickupSoundClientRpc(player.transform.position);
                }
            }
        }

        base.Die();
    }

    [ClientRpc]
    private void BroadcastMoneyPickupSoundClientRpc(Vector3 playerPosition)
    {
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySound(SoundType.MoneyPickup, playerPosition);
        }
    }

    protected void FindTarget()
    {
        Transform closestTarget = null;
        float bestDistanceSqr = float.MaxValue;
        Vector3 enemyPos = transform.position;

        IReadOnlyList<Player> players = Player.ActivePlayers;
        for (int i = 0; i < players.Count; i++)
        {
            Player player = players[i];
            if (player == null || !player.isActiveAndEnabled)
            {
                continue;
            }

            if (player.IsDead.Value || player.IsDown.Value)
            {
                continue; // Ignore dead or downed players
            }

            Vector3 delta = player.transform.position - enemyPos;
            float distanceSqr = delta.sqrMagnitude;
            if (distanceSqr < bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                closestTarget = player.transform;
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
        float moveDirection = Mathf.Sign(horizontalDelta);
        bool obstacleSenseDue = Time.time >= nextObstacleSenseTime || !hasCachedObstacleAhead;
        int obstacleHitCount = 0;

        if (obstacleSenseDue)
        {
            obstacleHitCount = GetObstacleHitCount(moveDirection);
            cachedObstacleAhead = HasBlockingObstacle(obstacleHitCount);
            hasCachedObstacleAhead = true;
            nextObstacleSenseTime = Time.time + Mathf.Max(0.02f, obstacleSenseInterval);
        }

        bool obstacleAhead = hasCachedObstacleAhead && cachedObstacleAhead;

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

        bool jumped = false;
        if (obstacleAhead && obstacleSenseDue)
        {
            jumped = TryJumpOverObstacle(moveDirection, obstacleHitCount);
        }

        if (obstacleAhead && !jumped)
        {
            float newVelX = Mathf.MoveTowards(
                rb.linearVelocity.x,
                0f,
                Mathf.Max(0f, deceleration) * Time.fixedDeltaTime
            );
            rb.linearVelocity = new Vector2(newVelX, rb.linearVelocity.y);
            return;
        }

        float moveX = moveDirection * Speed;
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

    private bool TryJumpOverObstacle(float moveDirection, int hitCount)
    {
        if (enemyCollider == null || rb == null) return false;
        if (moveDirection == 0f) return false;
        if (Time.time < nextJumpTime) return false;
        if (!CheckGrounded()) return false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = obstacleHitBuffer[i];
            if (hit.collider == null) continue;
            if (hit.collider == enemyCollider) continue;
            if (hit.collider.isTrigger) continue;

            Character character = hit.collider.GetComponentInParent<Character>();
            if (character != null && character != this)
            {
                continue;
            }

            rb.linearVelocity = new Vector2(rb.linearVelocity.x, Mathf.Max(rb.linearVelocity.y, jumpForce));
            nextJumpTime = Time.time + Mathf.Max(0.1f, jumpCooldown);
            return true;
        }

        return false;
    }

    private bool HasBlockingObstacle(int hitCount)
    {
        if (enemyCollider == null)
        {
            return false;
        }

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = obstacleHitBuffer[i];
            if (hit.collider == null) continue;
            if (hit.collider == enemyCollider) continue;
            if (hit.collider.isTrigger) continue;

            Character character = hit.collider.GetComponentInParent<Character>();
            if (character != null && character != this)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private int GetObstacleHitCount(float moveDirection)
    {
        Bounds bounds = enemyCollider.bounds;
        float sideOffset = Mathf.Max(0.01f, obstacleCastInset);
        Vector2 castSize = new Vector2(
            Mathf.Max(0.05f, bounds.size.x - sideOffset),
            Mathf.Max(0.05f, bounds.size.y - sideOffset)
        );
        Vector2 origin = new Vector2(
            bounds.center.x + (Mathf.Sign(moveDirection) * (bounds.extents.x + sideOffset)),
            bounds.center.y
        );

        return Physics2D.BoxCastNonAlloc(
            origin,
            castSize,
            0f,
            Vector2.right * Mathf.Sign(moveDirection),
            obstacleHitBuffer,
            Mathf.Max(0.05f, obstacleCheckDistance)
        );
    }

    private bool CheckGrounded()
    {
        if (enemyCollider == null)
        {
            return Mathf.Abs(rb.linearVelocity.y) < 0.01f;
        }

        Vector2 origin = new Vector2(
            enemyCollider.bounds.center.x,
            enemyCollider.bounds.min.y + 0.05f
        );

        int hitCount = Physics2D.RaycastNonAlloc(origin, Vector2.down, groundHitBuffer, 0.15f);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = groundHitBuffer[i];
            if (hit.collider == null) continue;
            if (hit.collider == enemyCollider) continue;
            if (hit.collider.isTrigger) continue;
            if (hit.collider.GetComponentInParent<Player>() != null) continue;

            if (hit.normal.y <= 0.5f) continue;

            return true;
        }

        return Mathf.Abs(rb.linearVelocity.y) < 0.01f;
    }

    private void TryDamagePlayerOverlap()
    {
        if (Time.time < nextTouchDamageTime || enemyCollider == null) return;

        int hitCount = Physics2D.OverlapBoxNonAlloc(
            enemyCollider.bounds.center,
            enemyCollider.bounds.size + new Vector3(0.2f, 0.2f, 0f),
            0f,
            overlapHitBuffer
        );

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D other = overlapHitBuffer[i];
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