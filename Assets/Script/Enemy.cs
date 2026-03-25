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
    [SerializeField] private float networkSmoothTime = 0.08f;
    [SerializeField] private float walkAnimThreshold = 0.05f;
    [SerializeField] private string walkBoolParam = "IsWalking";
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private bool spriteFacesRight = true;

    public float damage;
    protected Transform targetPlayer;
    private Rigidbody2D rb;
    private Collider2D enemyCollider;
    private Vector2 clientSmoothVelocity;
    private int walkBoolParamHash;

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
        if (IsServer)
        {
            netPosition.Value = rb.position;
            netFlipX.Value = spriteRenderer != null && spriteRenderer.flipX;
            netIsWalking.Value = Mathf.Abs(rb.linearVelocity.x) > walkAnimThreshold;
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
        netPosition.Value = rb.position;
        netFlipX.Value = spriteRenderer != null && spriteRenderer.flipX;
        bool isWalking = Mathf.Abs(rb.linearVelocity.x) > walkAnimThreshold;
        netIsWalking.Value = isWalking;

        if (animator != null)
        {
            animator.SetBool(walkBoolParamHash, isWalking);
        }
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
}
