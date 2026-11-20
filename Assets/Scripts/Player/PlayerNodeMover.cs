using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerNodeMover : MonoBehaviour
{
    [Header("Setup")]
    public RoomNode startRoom;
    [Tooltip("Camera whose look direction defines 'forward'. Falls back to Camera.main, then to this object.")]
    public Transform cameraTransform;
    [Tooltip("Optional. Drag your 'Move' action here if it isn't found automatically.")]
    public InputActionReference moveActionReference;

    [Header("Movement")]
    public float moveSpeed = 4f;
    public float minDuration = 0.8f;
    public float heightOffset = 1f;
    public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("How closely the chosen direction must match a neighbor (1 = exact, 0 = 90 degrees off).")]
    [Range(0f, 1f)] public float minAlignment = 0.1f;

    public RoomNode CurrentRoom { get; private set; }
    public bool IsMoving => moving;

    public bool BlockLeaving { get; set; }
    
    public event Action<RoomNode> LeaveBlocked;

    public bool InputLocked { get; private set; }

    public event Action<RoomNode> RoomEntered;

    public void SetInputLocked(bool locked)
    {
        InputLocked = locked;
        if (locked) inputArmed = false;
    }

    private static readonly MoveDirection[] AllDirections =
    {
        MoveDirection.Forward, MoveDirection.Back, MoveDirection.Left, MoveDirection.Right
    };

    private InputAction moveAction;
    private bool ownsEnable;
    private bool moving;
    private bool inputArmed = true;

    void Awake()
    {
        ResolveMoveAction();

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (cameraTransform == null)
        {
            Camera childCam = GetComponentInChildren<Camera>(true);
            if (childCam != null) cameraTransform = childCam.transform;
        }
    }

    void ResolveMoveAction()
    {
        if (moveActionReference != null)
            moveAction = moveActionReference.action;

        if (moveAction == null)
        {
            var playerInput = GetComponent<PlayerInput>();
            if (playerInput != null && playerInput.actions != null)
                moveAction = playerInput.actions.FindAction("Move");
        }

        if (moveAction == null && InputSystem.actions != null)
            moveAction = InputSystem.actions.FindAction("Move");
    }

    void OnEnable()
    {
        if (moveAction != null && !moveAction.enabled)
        {
            moveAction.Enable();
            ownsEnable = true;
        }
    }

    void OnDisable()
    {
        if (moveAction != null && ownsEnable)
        {
            moveAction.Disable();
            ownsEnable = false;
        }
    }

    void Start()
    {
        if (startRoom != null && CurrentRoom == null)
            InitializePlayer(startRoom);
    }

    public void InitializePlayer(RoomNode room)
    {
        StopAllCoroutines();
        moving = false;
        inputArmed = true;
        InputLocked = false;
        BlockLeaving = false;

        startRoom = room;
        CurrentRoom = room;
        transform.position = room.AnchorPosition + Vector3.up * heightOffset;
        transform.rotation = room.AnchorRotation;
        enabled = true;

        RoomEntered?.Invoke(room);
    }

    void Update()
    {
        if (PauseMenuController.BlocksGameplayInput) return;
        if (moving || InputLocked || CurrentRoom == null) return;

        Vector2 input = ReadInput();

        if (input.sqrMagnitude < 0.25f) { inputArmed = true; return; }
        if (!inputArmed) return;
        inputArmed = false;

        Vector3 desiredDir = GetDesiredWorldDirection(input);
        if (desiredDir == Vector3.zero) return;

        RoomNode target = FindBestNeighbor(desiredDir);
        if (target == null) return;

        if (BlockLeaving)
        {
            LeaveBlocked?.Invoke(target);
            return;
        }

        StartCoroutine(MoveTo(target));
    }

    Vector2 ReadInput()
    {
        Vector2 input = Vector2.zero;

        if (moveAction != null)
            input = moveAction.ReadValue<Vector2>();

        // An idle action legitimately returns zero. Only fall back when the
        // action is missing, so keyboard shortcuts cannot bypass saved remaps.
        if (moveAction == null && Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) input.y += 1f;
            if (Keyboard.current.sKey.isPressed) input.y -= 1f;
            if (Keyboard.current.aKey.isPressed) input.x -= 1f;
            if (Keyboard.current.dKey.isPressed) input.x += 1f;
        }

        return input;
    }

    Transform GetLookTransform()
    {
        if (cameraTransform != null) return cameraTransform;
        if (Camera.main != null) return Camera.main.transform;
        return transform;
    }

    Vector3 GetDesiredWorldDirection(Vector2 input)
    {
        Transform look = GetLookTransform();

        Vector3 fwd = Vector3.ProjectOnPlane(look.forward, Vector3.up);
        if (fwd.sqrMagnitude < 0.001f)
            fwd = Vector3.ProjectOnPlane(look.up, Vector3.up);
        fwd.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        return (fwd * input.y + right * input.x).normalized;
    }
    
    RoomNode FindBestNeighbor(Vector3 desiredDir)
    {
        RoomNode best = null;
        float bestDot = minAlignment;
        Vector3 origin = CurrentRoom.AnchorPosition;

        foreach (MoveDirection dir in AllDirections)
        {
            RoomNode neighbor = CurrentRoom.GetNeighbor(dir);
            if (neighbor == null) continue;

            Vector3 toNeighbor = neighbor.AnchorPosition - origin;
            toNeighbor.y = 0f;
            if (toNeighbor.sqrMagnitude < 0.0001f) continue;

            float dot = Vector3.Dot(desiredDir, toNeighbor.normalized);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = neighbor;
            }
        }

        return best;
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
            transform.position = Vector3.Lerp(startPos, endPos, ease.Evaluate(Mathf.Clamp01(t)));
            yield return null;
        }

        transform.position = endPos;
        CurrentRoom = target;
        moving = false;

        RoomEntered?.Invoke(target);
    }
}
