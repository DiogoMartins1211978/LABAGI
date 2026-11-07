using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(LiDAR_origin))]
public class PlayerNodeMover : MonoBehaviour
{
    [Header("Setup")]
    public RoomNode startRoom;

    [Header("Movement")]
    [Tooltip("Walking speed in meters per second. Longer distances take longer.")]
    public float moveSpeed = 4f;
    [Tooltip("Shortest allowed move time, so tiny hops still have room for the head turns")]
    public float minDuration = 0.8f;
    public float heightOffset = 1f;
    public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Head turn")]
    [Tooltip("Share of the move spent turning toward the destination at the start, and turning back to the room's facing at the end")]
    [Range(0.05f, 0.5f)] public float turnFraction = 0.3f;

    public RoomNode CurrentRoom { get; private set; }

    LiDAR_origin lidar;
    InputAction moveAction;
    bool moving;
    bool inputArmed = true;   // must return to neutral before the next step

    void Awake()
    {
        lidar = GetComponent<LiDAR_origin>();
        moveAction = InputSystem.actions.FindAction("Move");
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
        transform.SetPositionAndRotation(
            startRoom.AnchorPosition + Vector3.up * heightOffset,
            startRoom.AnchorRotation);

        lidar.range = startRoom.scanRange;
    }

    void Update()
    {
        if (moveAction == null || moving) return;

        Vector2 input = moveAction.ReadValue<Vector2>();

        // One step per push: wait until the input is released
        if (input.magnitude < 0.5f) { inputArmed = true; return; }
        if (!inputArmed) return;
        inputArmed = false;

        if (Mathf.Abs(input.x) > Mathf.Abs(input.y))
            TryMove(input.x > 0f ? MoveDirection.Right : MoveDirection.Left);
        else
            TryMove(input.y > 0f ? MoveDirection.Forward : MoveDirection.Back);
    }

    void TryMove(MoveDirection dir)
    {
        RoomNode target = CurrentRoom.GetNeighbor(dir);
        if (target != null) StartCoroutine(MoveTo(target));
    }

    IEnumerator MoveTo(RoomNode target)
    {
        moving = true;

        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;
        Vector3 endPos = target.AnchorPosition + Vector3.up * heightOffset;
        Quaternion endRot = target.AnchorRotation;
        float startRange = lidar.range;

        // Look toward where we're going (flat, so the head doesn't tilt)
        Vector3 travel = endPos - startPos;
        travel.y = 0f;
        Quaternion lookRot = travel.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(travel.normalized, Vector3.up)
            : startRot;

        // Longer walk = longer animation
        float duration = Mathf.Max(minDuration, Vector3.Distance(startPos, endPos) / Mathf.Max(0.01f, moveSpeed));

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float tc = Mathf.Clamp01(t);
            float k = ease.Evaluate(tc);

            // Turn toward the destination at the start...
            float turnIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, turnFraction, tc));
            // ...and back to the room's defined facing at the end
            float turnOut = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - turnFraction, 1f, tc));

            Quaternion rot = Quaternion.Slerp(startRot, lookRot, turnIn);
            rot = Quaternion.Slerp(rot, endRot, turnOut);

            transform.position = Vector3.Lerp(startPos, endPos, k);
            transform.rotation = rot;
            lidar.range = Mathf.Lerp(startRange, target.scanRange, k);

            yield return null;
        }

        transform.SetPositionAndRotation(endPos, endRot);
        CurrentRoom = target;
        moving = false;
    }
}
