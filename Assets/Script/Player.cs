using UnityEngine;
using Unity.Netcode;

public class Player : Character
{
    private Rigidbody2D rb;
    private Animator animator;

    private float moveInput;

    // ⭐ sync movement ให้ทุก client
    private NetworkVariable<float> netMoveInput =
        new NetworkVariable<float>(0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();

        Speed = 5f;
    }

    private void Update()
    {
        // Owner อ่าน input เท่านั้น
        if (IsOwner)
        {
            HandleInput();
        }

        // ทุกเครื่องเล่น animation
        HandleAnimation();
        Flip();
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;

        Move();
    }


    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        Camera.main
            .GetComponent<CameraFollow>()
            .target = transform;
    }


    public override void Move()
    {
        rb.linearVelocity = new Vector2(
            moveInput * Speed,
            rb.linearVelocity.y
        );
    }

    void HandleInput()
    {
        moveInput = Input.GetAxis("Horizontal");

        // ⭐ ส่งค่าไป network
        netMoveInput.Value = moveInput;
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
}