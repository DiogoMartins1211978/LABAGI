using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [Header("Look Settings")]
    [Tooltip("Mouse look sensitivity")]
    public float sensitivity = 0.2f;

    [Tooltip("Keyboard camera rotation in degrees per second")]
    public float keyboardLookSpeed = 90f;

    private InputAction keyboardLook;

    [Tooltip("Maximum vertical angle (45 degrees up and 45 degrees down)")]
    public float maxVerticalAngle = 20f;

    private float verticalRotation = 0f;
    private float horizontalRotation = 0f;

    void Start()
    {
        keyboardLook = InputSystem.actions?.FindAction("Player/KeyboardLook");
        keyboardLook?.Enable();
        Vector3 angles = transform.localEulerAngles;
        horizontalRotation = angles.y;
        verticalRotation = angles.x;
    }

    void Update()
    {
        if (PauseMenuController.BlocksGameplayInput) return;
        Vector2 mouseDelta = Mouse.current != null
            ? Mouse.current.delta.ReadValue() * sensitivity : Vector2.zero;
        Vector2 keyDelta = keyboardLook != null ? keyboardLook.ReadValue<Vector2>() : Vector2.zero;
        mouseDelta += keyDelta * keyboardLookSpeed * Time.deltaTime;

        horizontalRotation += mouseDelta.x;
        verticalRotation -= mouseDelta.y;

        verticalRotation = Mathf.Clamp(verticalRotation, -maxVerticalAngle, maxVerticalAngle);

        transform.localRotation = Quaternion.Euler(verticalRotation, horizontalRotation, 0f);
    }

    public void RestoreSavedRotation(Quaternion rotation)
    {
        Vector3 angles = rotation.eulerAngles;
        horizontalRotation = angles.y;
        verticalRotation = Mathf.Clamp(Mathf.DeltaAngle(0f, angles.x), -maxVerticalAngle, maxVerticalAngle);
        transform.localRotation = Quaternion.Euler(verticalRotation, horizontalRotation, 0f);
    }
}
