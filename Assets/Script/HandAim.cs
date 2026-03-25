using UnityEngine;
using Unity.Netcode;

public class HandAim : NetworkBehaviour
{
    public Transform pivot;
    public Transform playerBody;
    public Vector3 handOffsetRight = new Vector3(0.4f, 0f, 0f);
    public Vector3 handOffsetLeft = new Vector3(-0.4f, 0f, 0f);
    public float moveThreshold = 0.05f;
    public float shakeAmount = 0.04f;
    public float shakeSpeed = 14f;
    public float shakeFrameRate = 12f;
    public float horizontalShakeMultiplier = 1f;
    public float verticalShakeMultiplier = 1.4f;
    public float walkDownOffset = 0.06f;
    public float walkDownSmooth = 10f;
    public float recoilDistance = 0.46f;
    public float recoilAngle = 20f;
    public float recoilSnap = 92f;
    public float recoilRecover = 28f;
    public float recoilDamping = 10f;
    public float recoilRandomAngle = 2.5f;
    public float recoilMaxDistance = 0.9f;
    public float recoilMaxAngle = 36f;
    public float networkSendRate = 20f;
    public float networkPositionThreshold = 0.002f;
    public float networkAngleThreshold = 0.5f;
    public float remoteLerpSpeed = 20f;

    private NetworkVariable<Vector2> netPivotOffset = new NetworkVariable<Vector2>(
        Vector2.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );
    private NetworkVariable<float> netPivotAngle = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );
    private NetworkVariable<bool> netAimInitialized = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private Vector3 lastPlayerPosition;
    private bool hasLastPlayerPosition;
    private Vector3 currentShakeOffset;
    private float currentWalkDown;
    private float recoilPosition;
    private float recoilPositionVelocity;
    private float recoilRotation;
    private float recoilRotationVelocity;
    private float nextNetworkSendTime;
    private Vector2 lastSentPivotOffset;
    private float lastSentPivotAngle;
    private bool hasSentAimState;

    public override void OnNetworkSpawn()
    {
        if (pivot == null || playerBody == null) return;

        if (IsOwner)
        {
            Vector2 startOffset = GetCurrentBodyRelativeOffset();
            float startAngle = pivot.eulerAngles.z;

            netPivotOffset.Value = startOffset;
            netPivotAngle.Value = startAngle;
            netAimInitialized.Value = true;

            lastSentPivotOffset = startOffset;
            lastSentPivotAngle = startAngle;
            hasSentAimState = true;
        }
    }

    Vector2 GetCurrentBodyRelativeOffset()
    {
        if (pivot == null || playerBody == null)
        {
            return Vector2.zero;
        }

        if (pivot.parent == playerBody)
        {
            return pivot.localPosition;
        }

        return (Vector2)(pivot.position - playerBody.position);
    }

    void ApplyBodyRelativeOffset(Vector2 offset, float t)
    {
        if (pivot == null || playerBody == null) return;

        if (pivot.parent == playerBody)
        {
            Vector3 targetLocal = new Vector3(offset.x, offset.y, pivot.localPosition.z);
            pivot.localPosition = Vector3.Lerp(pivot.localPosition, targetLocal, t);
            return;
        }

        Vector3 targetWorld = new Vector3(
            playerBody.position.x + offset.x,
            playerBody.position.y + offset.y,
            pivot.position.z
        );
        pivot.position = Vector3.Lerp(pivot.position, targetWorld, t);
    }

    public void AddRecoil(float amount = 1f)
    {
        float kickAmount = Mathf.Max(0f, amount);
        float angleJitter = Random.Range(-recoilRandomAngle, recoilRandomAngle);

        AddRecoilWithJitter(kickAmount, angleJitter);
    }

    public void AddRecoilWithJitter(float amount, float angleJitter)
    {
        float kickAmount = Mathf.Max(0f, amount);
        float clampedJitter = Mathf.Clamp(angleJitter, -Mathf.Abs(recoilRandomAngle), Mathf.Abs(recoilRandomAngle));

        // Add impulse velocity so recoil hits instantly, then spring recovery handles the return.
        recoilPositionVelocity += kickAmount * recoilDistance * recoilSnap;
        recoilRotationVelocity += kickAmount * (recoilAngle + clampedJitter) * recoilSnap;
    }

    void Update()
    {
        if (pivot == null) return;

        if (IsOwner)
        {
            OwnerUpdate();
            return;
        }

        RemoteUpdate();
    }

    void OwnerUpdate()
    {
        if (playerBody == null) return;

        if (!hasLastPlayerPosition)
        {
            lastPlayerPosition = playerBody.position;
            hasLastPlayerPosition = true;
        }

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float speed = (playerBody.position - lastPlayerPosition).magnitude / dt;
        bool isMoving = speed > moveThreshold;
        lastPlayerPosition = playerBody.position;

        bool isFacingLeft = playerBody.localScale.x < 0;
        Vector3 baseOffset = isFacingLeft ? handOffsetLeft : handOffsetRight;

        Vector3 targetShakeOffset = Vector3.zero;
        if (isMoving)
        {
            float frameRate = Mathf.Max(shakeFrameRate, 1f);
            float steppedTime = Mathf.Floor(Time.time * frameRate) / frameRate;
            float t = steppedTime * shakeSpeed;
            targetShakeOffset = new Vector3(
                Mathf.Sin(t) * shakeAmount * horizontalShakeMultiplier,
                Mathf.Cos(t * 1.35f) * shakeAmount * verticalShakeMultiplier,
                0f
            );
        }

        float targetWalkDown = isMoving ? walkDownOffset : 0f;
        currentWalkDown = Mathf.Lerp(currentWalkDown, targetWalkDown, Time.deltaTime * walkDownSmooth);

        float spring = recoilRecover * recoilRecover;
        float damping = 2f * recoilDamping;

        recoilPositionVelocity += (-recoilPosition * spring - recoilPositionVelocity * damping) * dt;
        recoilPosition += recoilPositionVelocity * dt;

        recoilRotationVelocity += (-recoilRotation * spring - recoilRotationVelocity * damping) * dt;
        recoilRotation += recoilRotationVelocity * dt;

        recoilPosition = Mathf.Clamp(recoilPosition, 0f, recoilMaxDistance);
        recoilRotation = Mathf.Clamp(recoilRotation, 0f, recoilMaxAngle);

        Vector3 mouse = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouse.z = 0;
        Vector2 aimDir = (mouse - pivot.position);
        if (aimDir.sqrMagnitude > 0.0001f)
        {
            aimDir.Normalize();
        }
        else
        {
            aimDir = isFacingLeft ? Vector2.left : Vector2.right;
        }

        currentShakeOffset = targetShakeOffset;
        Vector3 recoilOffset = (Vector3)(-aimDir * recoilPosition);
        Vector3 finalOffset = baseOffset + currentShakeOffset + new Vector3(0f, -currentWalkDown, 0f) + recoilOffset;

        if (pivot.parent == playerBody)
        {
            // If pivot is parented to playerBody, keep hand in local offset space.
            pivot.localPosition = finalOffset;
        }
        else
        {
            // If pivot is not parented, place hand in world space from player position.
            pivot.position = playerBody.position + finalOffset;
        }

        Vector2 dir = mouse - pivot.position;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float recoilRotationOffset = isFacingLeft ? recoilRotation : -recoilRotation;

        // ถ้าตัวละครกลับด้าน ต้องแก้มุมปืน
        if (isFacingLeft)
        {
            pivot.rotation = Quaternion.Euler(0, 0, angle + recoilRotationOffset);
        }
        else
        {
            pivot.rotation = Quaternion.Euler(0, 0, angle + 180 + recoilRotationOffset);
        }

        PublishAimState();
    }

    void PublishAimState()
    {
        float safeSendRate = Mathf.Max(1f, networkSendRate);
        if (Time.time < nextNetworkSendTime) return;

        Vector2 offset = GetCurrentBodyRelativeOffset();
        float angle = pivot.eulerAngles.z;

        bool movedEnough = !hasSentAimState || Vector2.Distance(offset, lastSentPivotOffset) >= networkPositionThreshold;
        bool turnedEnough = !hasSentAimState || Mathf.Abs(Mathf.DeltaAngle(lastSentPivotAngle, angle)) >= networkAngleThreshold;
        if (!movedEnough && !turnedEnough) return;

        netPivotOffset.Value = offset;
        netPivotAngle.Value = angle;

        lastSentPivotOffset = offset;
        lastSentPivotAngle = angle;
        hasSentAimState = true;
        nextNetworkSendTime = Time.time + (1f / safeSendRate);
    }

    void RemoteUpdate()
    {
        if (playerBody == null || !netAimInitialized.Value) return;

        Vector2 targetOffset = netPivotOffset.Value;
        float targetAngle = netPivotAngle.Value;
        float t = Mathf.Clamp01(Time.deltaTime * Mathf.Max(1f, remoteLerpSpeed));

        ApplyBodyRelativeOffset(targetOffset, t);

        float currentZ = pivot.eulerAngles.z;
        float z = Mathf.LerpAngle(currentZ, targetAngle, t);
        pivot.rotation = Quaternion.Euler(0f, 0f, z);
    }
}