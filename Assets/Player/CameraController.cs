using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [Header("Look Settings")]
    [Tooltip("Mouse look sensitivity")]
    public float sensitivity = 0.2f;

    [Tooltip("Maximum vertical angle (45 degrees up and 45 degrees down)")]
    public float maxVerticalAngle = 20f;

    private float verticalRotation = 0f;
    private float horizontalRotation = 0f;

    void Start()
    {
        Vector3 angles = transform.localEulerAngles;
        horizontalRotation = angles.y;
        verticalRotation = angles.x;
    }

    void Update()
    {
        if (Mouse.current == null) return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * sensitivity;

        horizontalRotation += mouseDelta.x;
        verticalRotation -= mouseDelta.y;

        verticalRotation = Mathf.Clamp(verticalRotation, -maxVerticalAngle, maxVerticalAngle);

        transform.localRotation = Quaternion.Euler(verticalRotation, horizontalRotation, 0f);
    }
}