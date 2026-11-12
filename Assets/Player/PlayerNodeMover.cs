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

        startRoom = room;
        CurrentRoom = room;
        transform.position = room.AnchorPosition + Vector3.up * heightOffset;
        transform.rotation = room.AnchorRotation;
        enabled = true;
    }

    void Update()
    {
        if (moving || CurrentRoom == null) return;

        Vector2 input = ReadInput();

        // One step per key press: must release before the next move.
        if (input.sqrMagnitude < 0.25f) { inputArmed = true; return; }
        if (!inputArmed) return;
        inputArmed = false;

        Vector3 desiredDir = GetDesiredWorldDirection(input);
        if (desiredDir == Vector3.zero) return;

        RoomNode target = FindBestNeighbor(desiredDir);
        if (target != null)
            StartCoroutine(MoveTo(target));
    }

    Vector2 ReadInput()
    {
        Vector2 input = Vector2.zero;

        if (moveAction != null)
            input = moveAction.ReadValue<Vector2>();

        // Keyboard backup if the action isn't hooked up or returns nothing.
        if (input == Vector2.zero && Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) input.y += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) input.y -= 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input.x -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input.x += 1f;
        }

        return input;
    }

    Transform GetLookTransform()
    {
        if (cameraTransform != null) return cameraTransform;
        if (Camera.main != null) return Camera.main.transform;
        return transform; // last resort so movement never silently dies
    }

    // Converts WASD into a world direction based on where the camera is looking.
    Vector3 GetDesiredWorldDirection(Vector2 input)
    {
        Transform look = GetLookTransform();

        Vector3 fwd = Vector3.ProjectOnPlane(look.forward, Vector3.up);
        if (fwd.sqrMagnitude < 0.001f)
            fwd = Vector3.ProjectOnPlane(look.up, Vector3.up); // looking straight up/down
        fwd.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        return (fwd * input.y + right * input.x).normalized;
    }

    // Picks the connected room whose real direction best matches the desired direction.
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
    }
}