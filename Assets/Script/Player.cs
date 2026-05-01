using UnityEngine;
using Unity.Netcode;

public class Player : Character
{
    [SerializeField] private float networkSendRate = 20f;
    [SerializeField] private float inputSendThreshold = 0.02f;
    [SerializeField] private float positionSendThreshold = 0.02f;
    [SerializeField] private float remoteSmoothTime = 0.08f;

    private Rigidbody2D rb;
    private Animator animator;

    private float moveInput;
    private float nextInputSendTime;
    private float nextPositionSendTime;
    private float lastSentMoveInput;
    private Vector2 lastSentPosition;
    private bool hasSentMoveInput;
    private bool hasSentPosition;
    private Vector2 remoteSmoothVelocity;
    private Collider2D[] playerColliders;
    private SpriteRenderer[] playerRenderers;
    private Gun[] playerGuns;

    // ⭐ sync movement ให้ทุก client
    private NetworkVariable<float> netMoveInput =
        new NetworkVariable<float>(0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);
    private NetworkVariable<Vector2> netPosition =
        new NetworkVariable<Vector2>(
            Vector2.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

    private void Awake()
    {
        SetDespawnOnDeath(false);

        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        playerColliders = GetComponentsInChildren<Collider2D>(true);
        playerRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        playerGuns = GetComponentsInChildren<Gun>(true);
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        Speed = 5f;
    }

    private void Update()
    {
        if (IsDead.Value)
        {
            return;
        }

        // Owner อ่าน input เท่านั้น
        if (IsOwner)
        {
            HandleInput();
        }
        else
        {
            SmoothRemoteMovement();
        }

        // ทุกเครื่องเล่น animation
        HandleAnimation();
        Flip();
    }

    private void FixedUpdate()
    {
        if (IsDead.Value)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        if (!IsOwner) return;

        Move();
    }


    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        IsDead.OnValueChanged += OnDeadStateChanged;
        ApplyDeadState(IsDead.Value);

        if (IsOwner)
        {
            netMoveInput.Value = moveInput;
            netPosition.Value = rb.position;
            lastSentMoveInput = moveInput;
            lastSentPosition = rb.position;
            hasSentMoveInput = true;
            hasSentPosition = true;
        }
        else
        {
            rb.position = netPosition.Value;
        }

        if (!IsOwner) return;

        Camera.main
            .GetComponent<CameraFollow>()
            .target = transform;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        IsDead.OnValueChanged -= OnDeadStateChanged;
    }


    public override void Move()
    {
        rb.linearVelocity = new Vector2(
            moveInput * Speed,
            rb.linearVelocity.y
        );

        PublishPosition();
    }

    void HandleInput()
    {
        moveInput = Input.GetAxisRaw("Horizontal");

        float safeRate = Mathf.Max(1f, networkSendRate);
        if (Time.time < nextInputSendTime)
        {
            return;
        }

        bool changedEnough = !hasSentMoveInput || Mathf.Abs(moveInput - lastSentMoveInput) >= inputSendThreshold;
        if (!changedEnough)
        {
            return;
        }

        // ⭐ ส่งค่าไป network
        netMoveInput.Value = moveInput;
        lastSentMoveInput = moveInput;
        hasSentMoveInput = true;
        nextInputSendTime = Time.time + (1f / safeRate);
    }

    void PublishPosition()
    {
        float safeRate = Mathf.Max(1f, networkSendRate);
        if (Time.time < nextPositionSendTime)
        {
            return;
        }

        Vector2 currentPosition = rb.position;
        bool changedEnough = !hasSentPosition || Vector2.Distance(currentPosition, lastSentPosition) >= positionSendThreshold;
        if (!changedEnough)
        {
            return;
        }

        netPosition.Value = currentPosition;
        lastSentPosition = currentPosition;
        hasSentPosition = true;
        nextPositionSendTime = Time.time + (1f / safeRate);
    }

    void SmoothRemoteMovement()
    {
        Vector2 targetPosition = netPosition.Value;
        rb.position = Vector2.SmoothDamp(
            rb.position,
            targetPosition,
            ref remoteSmoothVelocity,
            Mathf.Max(0.01f, remoteSmoothTime)
        );
    }

    void HandleAnimation()
    {
        float speed = Mathf.Abs(netMoveInput.Value);
        animator.SetFloat("Speed", speed);
    }

    void Flip()
    {
        Vector3 scale = transform.localScale;

        float dir = netMoveInput.Value;

        if (dir > 0)
            scale.x = -Mathf.Abs(scale.x);
        else if (dir < 0)
            scale.x = Mathf.Abs(scale.x);

        transform.localScale = scale;
    }

    private void OnDeadStateChanged(bool previousValue, bool newValue)
    {
        ApplyDeadState(newValue);
    }

    private void ApplyDeadState(bool isDead)
    {
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.simulated = !isDead;
        }

        if (animator != null)
        {
            animator.enabled = !isDead;
        }

        for (int i = 0; i < playerColliders.Length; i++)
        {
            if (playerColliders[i] != null)
            {
                playerColliders[i].enabled = !isDead;
            }
        }

        for (int i = 0; i < playerRenderers.Length; i++)
        {
            if (playerRenderers[i] != null)
            {
                playerRenderers[i].enabled = !isDead;
            }
        }

        for (int i = 0; i < playerGuns.Length; i++)
        {
            if (playerGuns[i] != null)
            {
                playerGuns[i].enabled = !isDead;
            }
        }
    }

    public override void ServerRespawn(Vector3 worldPosition)
    {
        base.ServerRespawn(worldPosition);
        ApplyRespawnClientRpc(worldPosition);
    }

    [ClientRpc]
    private void ApplyRespawnClientRpc(Vector3 worldPosition)
    {
        if (rb == null)
        {
            return;
        }

        rb.position = worldPosition;

        if (IsOwner)
        {
            netPosition.Value = worldPosition;
            lastSentPosition = worldPosition;
            hasSentPosition = true;
        }
    }
}