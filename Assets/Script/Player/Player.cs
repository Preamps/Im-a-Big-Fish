using UnityEngine;
using Unity.Netcode;
using Unity.Collections;

public class Player : NetworkBehaviour
{
    [Header("Player Data")]
    public NetworkVariable<FixedString32Bytes> PlayerName = new NetworkVariable<FixedString32Bytes>(
        "Player",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    [Header("Movement Settings")]
    [SerializeField] private float speed = 5f;

    private Rigidbody2D rb;
    private Animator animator;
    private float moveInput;

    public override void OnNetworkSpawn()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();

        // --- ส่วนการดึงชื่อจาก Lobby ---
        if (IsOwner)
        {
            string savedName = PlayerPrefs.GetString("PlayerName", "Player " + OwnerClientId);
            PlayerName.Value = savedName;
        }
    }

    private void Update()
    {
        // สำคัญ: ถ้าไม่ใช่เจ้าของตัวละครตัวนี้ ห้ามประมวลผลการควบคุม
        if (!IsOwner) return;

        Move();
        HandleAnimation();
        Flip();
    }

    private void Move()
    {
        moveInput = Input.GetAxisRaw("Horizontal");
        // ใน Unity เวอร์ชั่นใหม่ๆใช้ .linearVelocity แทน .velocity
        rb.linearVelocity = new Vector2(moveInput * speed, rb.linearVelocity.y);
    }

    private void HandleAnimation()
    {
        if (animator != null)
        {
            animator.SetFloat("Speed", Mathf.Abs(moveInput));
        }
    }

    private void Flip()
    {
        if (moveInput == 0) return;
        Vector3 scale = transform.localScale;
        scale.x = moveInput > 0 ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x);
        transform.localScale = scale;
    }
}