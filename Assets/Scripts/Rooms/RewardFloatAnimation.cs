
using UnityEngine;

public class RewardFloatAnimation : MonoBehaviour
{
    [Header("Floating")]
    public float floatHeight = 0.08f;
    public float floatSpeed = 2f;

    [Header("Rotation")]
    public float rotationAngle = 8f;
    public float rotationSpeed = 1.5f;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private float randomOffset;

    private void Start()
    {
        initialPosition = transform.localPosition;
        initialRotation = transform.localRotation;
        randomOffset = Random.Range(0f, 2f * Mathf.PI);
    }

    private void Update()
    {
        float time = Time.time + randomOffset;

        float verticalOffset =
            Mathf.Sin(time * floatSpeed) * floatHeight;

        transform.localPosition =
            initialPosition + Vector3.up * verticalOffset;

        float rotation =
            Mathf.Sin(time * rotationSpeed) * rotationAngle;

        transform.localRotation =
            initialRotation * Quaternion.Euler(0f, rotation, 0f);
    }
}
