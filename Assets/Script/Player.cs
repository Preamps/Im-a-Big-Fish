using UnityEngine;

public class Player : Character
{
    private Rigidbody2D rb;
    private Animator animator;

    private float moveInput;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();

        Name = "Player";
        Health = 100f;
        Speed = 5f;
    }

    private void Update()
    {
        Move();
        HandleAnimation();
        Flip();
    }

    public override void Move()
    {
        moveInput = Input.GetAxis("Horizontal");

        rb.linearVelocity = new Vector2(
            moveInput * Speed,
            rb.linearVelocity.y
        );
    }

    void HandleAnimation()
    {
        animator.SetFloat("Speed", Mathf.Abs(moveInput));
    }

    void Flip()
    {
        Vector3 scale = transform.localScale;

        if (moveInput > 0)
            scale.x = -Mathf.Abs(scale.x);
        else if (moveInput < 0)
            scale.x = Mathf.Abs(scale.x);

        transform.localScale = scale;
    }
}