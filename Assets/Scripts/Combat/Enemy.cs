using UnityEngine;
using System;
using System.Collections;

public class Enemy : MonoBehaviour
{
    [Header("Detection (how the player 'finds' this enemy)")]
    [Tooltip("Max distance the player can spot it from. 0 = use the room's scanRange.")]
    public float spotRange = 0f;
    [Tooltip("Half-angle of the player's view cone. Smaller = player must look more directly at it.")]
    [Range(1f, 90f)] public float spotAngle = 35f;
    [Tooltip("If true, walls between the camera and the enemy block spotting.")]
    public bool requireLineOfSight = true;
    public LayerMask lineOfSightMask = ~0;

    [Header("Combat camera")]
    [Tooltip("Optional. The point the combat camera stares at (e.g. a child at head height).")]
    public Transform focusPoint;
    [Tooltip("Used when no focusPoint is assigned.")]
    public float focusHeight = 1.2f;

    [Header("Combat Animation")]
    public Animator animator;

    private Action attackFinished;

    public float attackDuration = 1.5f;

    private Coroutine attackCoroutine;

    public RoomNode Room { get; set; }
    public bool Resolved { get; private set; }

    public Vector3 FocusPosition =>
        focusPoint != null ? focusPoint.position : transform.position + Vector3.up * focusHeight;

    public bool IsSeenBy(Transform viewer)
    {
        if (Resolved || viewer == null) return false;

        float range = spotRange > 0f ? spotRange : (Room != null ? Room.scanRange : 12f);

        Vector3 origin = viewer.position;
        Vector3 toEnemy = FocusPosition - origin;
        float dist = toEnemy.magnitude;

        if (dist > range) return false;
        if (dist < 0.01f) return true;
        if (Vector3.Angle(viewer.forward, toEnemy) > spotAngle) return false;
        if (!requireLineOfSight) return true;

        if (Physics.Raycast(origin, toEnemy / dist, out RaycastHit hit, dist, lineOfSightMask, QueryTriggerInteraction.Ignore))
            return hit.transform == transform || hit.transform.IsChildOf(transform);

        return true;
    }

    public virtual void OnSpotted()
    {
        Debug.Log($"[Enemy] '{name}' was spotted by the player.");
    }

    public virtual void OnSurpriseAttack()
    {
        Debug.Log($"[Enemy] '{name}' surprise attacks the player!");
    }

    public virtual void Attack(Action onFinished = null)
    {
        if (Resolved || animator == null)
        {
            onFinished?.Invoke();
            return;
        }

        Debug.Log($"[Enemy] {name} started attack.");

        attackFinished = onFinished;

        animator.SetBool("isAttacking", true);

        if (attackCoroutine != null)
            StopCoroutine(attackCoroutine);

        attackCoroutine = StartCoroutine(AttackRoutine());
    }

    private IEnumerator AttackRoutine()
    {
        yield return new WaitForSeconds(attackDuration);

        EndAttack();
    }

    public virtual void EndAttack()
    {
        Debug.Log($"[Enemy] {name} finished attack.");

        if (animator != null)
            animator.SetBool("isAttacking", false);

        Action callback = attackFinished;
        attackFinished = null;
        attackCoroutine = null;

        callback?.Invoke();
    }

    public virtual void Resolve()
    {
        Resolved = true;
        gameObject.SetActive(false);
    }
}