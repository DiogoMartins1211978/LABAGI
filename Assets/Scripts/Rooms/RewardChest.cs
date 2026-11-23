
using System.Collections;
using UnityEngine;

public class RewardChest : MonoBehaviour
{
    public enum OpeningType
    {
        None,
        ProceduralLid,
        Animator,
        ProceduralNoLid
    }

    [Header("Orientation")]
    [Tooltip("Assign the centre of the room this chest belongs to.")]
    public Transform roomCenter;

    [Header("Animation")]
    public OpeningType openingType = OpeningType.ProceduralLid;

    [Tooltip("The transform located at the hinge of the lid.")]
    public Transform lidPivot;

    [Tooltip("Local Euler rotation added to the closed lid rotation.")]
    public Vector3 openEulerAngles = new Vector3(-100f, 0f, 0f);

    public float openingDuration = 0.8f;

    [Tooltip("Used when Opening Type is Animator.")]
    public Animator animator;

    public string openTrigger = "Open";

    [Header("Interaction Points")]
    public Transform focusPoint;
    public Transform rewardSpawnPoint;

    [Header("Reward Data")]
    public CardData cardReward;
    public GameObject cardVisualPrefab;
    public GameObject heartVisualPrefab;

    [Header("Interaction")]
    public Collider interactionCollider;

    [Header("Procedural No-Lid Animation")]
    public float shakeAmount = 3f;
    public float shakeSpeed = 35f;
    public float liftAmount = 0.12f;

    public bool IsClaimed { get; private set; }
    public bool IsOpened { get; private set; }

    private Quaternion closedLidRotation;
    private Quaternion openLidRotation;

    private Vector3 initialPosition;
    private Quaternion initialRotation;

    private void Awake()
    {
        initialPosition = transform.localPosition;
        initialRotation = transform.localRotation;

        if (lidPivot != null)
        {
            closedLidRotation = lidPivot.localRotation;
            openLidRotation = closedLidRotation *
                              Quaternion.Euler(openEulerAngles);
        }

        if (focusPoint == null)
            focusPoint = transform;

        if (rewardSpawnPoint == null)
            rewardSpawnPoint = transform;

        if (interactionCollider == null)
            interactionCollider = GetComponentInChildren<Collider>();
    }

    private void Start()
    {
        FaceRoomCenter();
    }

    private void FaceRoomCenter()
    {
        if (roomCenter == null)
            return;

        Vector3 direction = roomCenter.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            return;

        transform.rotation = Quaternion.LookRotation(
            direction.normalized,
            Vector3.up
        );
    }

    public IEnumerator Open()
    {
        if (IsOpened)
            yield break;

        IsOpened = true;

        switch (openingType)
        {
            case OpeningType.None:
                yield break;

            case OpeningType.ProceduralLid:
                yield return OpenProceduralLid();
                break;

            case OpeningType.Animator:
                yield return OpenAnimator();
                break;

            case OpeningType.ProceduralNoLid:
                yield return OpenProceduralNoLid();
                break;
        }
    }

    private IEnumerator OpenProceduralLid()
    {
        if (lidPivot == null)
        {
            Debug.LogWarning(
                $"[RewardChest] No Lid Pivot assigned on {name}."
            );
            yield break;
        }

        float duration = Mathf.Max(0.01f, openingDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            lidPivot.localRotation = Quaternion.Slerp(
                closedLidRotation,
                openLidRotation,
                smoothT
            );

            yield return null;
        }

        lidPivot.localRotation = openLidRotation;
    }

    private IEnumerator OpenAnimator()
    {
        if (animator == null)
        {
            Debug.LogWarning(
                $"[RewardChest] No Animator assigned on {name}."
            );
            yield break;
        }

        if (!string.IsNullOrEmpty(openTrigger))
            animator.SetTrigger(openTrigger);

        yield return new WaitForSeconds(openingDuration);
    }

    private IEnumerator OpenProceduralNoLid()
    {
        float duration = Mathf.Max(0.01f, openingDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / duration);
            float envelope = Mathf.Sin(t * Mathf.PI);

            float shake = Mathf.Sin(elapsed * shakeSpeed) *
                          shakeAmount * envelope;

            transform.localRotation =
                initialRotation * Quaternion.Euler(0f, shake, 0f);

            transform.localPosition =
                initialPosition + Vector3.up * (liftAmount * envelope);

            yield return null;
        }

        transform.localPosition = initialPosition;
        transform.localRotation = initialRotation;
    }

    public void MarkClaimed()
    {
        IsClaimed = true;

        if (interactionCollider != null)
            interactionCollider.enabled = false;
    }

    public void RestoreClaimedState(bool claimed)
    {
        if (!claimed) return; // Newly instantiated chests are already closed and available.
        IsOpened = true;
        MarkClaimed();
        if (openingType == OpeningType.ProceduralLid && lidPivot != null)
            lidPivot.localRotation = openLidRotation;
        else if (openingType == OpeningType.Animator && animator != null && !string.IsNullOrEmpty(openTrigger))
        {
            // Settle to the opened pose without replaying the reward interaction.
            animator.SetTrigger(openTrigger);
            animator.Update(Mathf.Max(openingDuration, 0.01f));
        }
    }
}
