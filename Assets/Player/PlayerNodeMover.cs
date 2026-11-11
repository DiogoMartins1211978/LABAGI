using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerNodeMover : MonoBehaviour
{
    [Header("Setup")]
    public RoomNode startRoom;
    [Tooltip("Assign your camera transform here so movement knows where you are looking")]
    public Transform cameraTransform;

    [Header("Movement")]
    public float moveSpeed = 4f;
    public float minDuration = 0.8f;
    public float heightOffset = 1f;
    public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    public RoomNode CurrentRoom { get; private set; }

    InputAction moveAction;
    bool moving;
    bool inputArmed = true;

    void Awake()
    {
        moveAction = InputSystem.actions.FindAction("Move");

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    void Start()
    {
        if (startRoom == null)
        {
            Debug.LogError("PlayerNodeMover: assign a Start Room.");
            enabled = false;
            return;
        }

        CurrentRoom = startRoom;
        transform.position = startRoom.AnchorPosition + Vector3.up * heightOffset;
        transform.rotation = startRoom.AnchorRotation;
    }

    void Update()
    {
        if (moveAction == null || moving || CurrentRoom == null) return;

        Vector2 input = moveAction.ReadValue<Vector2>();

        if (input.magnitude < 0.5f) { inputArmed = true; return; }
        if (!inputArmed) return;
        inputArmed = false;

        Transform cam = cameraTransform != null ? cameraTransform : (Camera.main != null ? Camera.main.transform : null);
        if (cam == null) return;

        Vector3 camForward = cam.forward;
        camForward.y = 0f;
        camForward.Normalize();

        Vector3 camRight = cam.right;
        camRight.y = 0f;
        camRight.Normalize();

        Vector3 desiredWorldDir = (camForward * input.y + camRight * input.x).normalized;

        RoomNode bestTarget = null;
        float bestDot = -999f;

        if (CurrentRoom.forward != null)
        {
            Vector3 neighborDir = (CurrentRoom.forward.AnchorPosition - CurrentRoom.AnchorPosition).normalized;
            float dot = Vector3.Dot(desiredWorldDir, neighborDir);
            if (dot > bestDot) { bestDot = dot; bestTarget = CurrentRoom.forward; }
        }
        if (CurrentRoom.back != null)
        {
            Vector3 neighborDir = (CurrentRoom.back.AnchorPosition - CurrentRoom.AnchorPosition).normalized;
            float dot = Vector3.Dot(desiredWorldDir, neighborDir);
            if (dot > bestDot) { bestDot = dot; bestTarget = CurrentRoom.back; }
        }
        if (CurrentRoom.left != null)
        {
            Vector3 neighborDir = (CurrentRoom.left.AnchorPosition - CurrentRoom.AnchorPosition).normalized;
            float dot = Vector3.Dot(desiredWorldDir, neighborDir);
            if (dot > bestDot) { bestDot = dot; bestTarget = CurrentRoom.left; }
        }
        if (CurrentRoom.right != null)
        {
            Vector3 neighborDir = (CurrentRoom.right.AnchorPosition - CurrentRoom.AnchorPosition).normalized;
            float dot = Vector3.Dot(desiredWorldDir, neighborDir);
            if (dot > bestDot) { bestDot = dot; bestTarget = CurrentRoom.right; }
        }

        if (bestTarget != null && bestDot > 0.1f)
        {
            StartCoroutine(MoveTo(bestTarget));
        }
    }

    IEnumerator MoveTo(RoomNode target)
    {
        moving = true;

        Vector3 startPos = transform.position;
        Vector3 endPos = target.AnchorPosition + Vector3.up * heightOffset;

        float duration = Mathf.Max(minDuration, Vector3.Distance(startPos, endPos) / Mathf.Max(0.01f, moveSpeed));

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float tc = Mathf.Clamp01(t);
            float k = ease.Evaluate(tc);

            transform.position = Vector3.Lerp(startPos, endPos, k);

            yield return null;
        }

        transform.position = endPos;
        CurrentRoom = target;
        moving = false;
    }
}