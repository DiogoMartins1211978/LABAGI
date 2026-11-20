using UnityEngine;
using System;
using System.Collections;

public class Enemy : MonoBehaviour
{
    public struct SurfaceAttachment
    {
        public int TriangleIndex;
        public Vector3 BarycentricCoordinate;
    }

    [Header("Detection")]
    public float spotRange = 0f;
    [Range(1f, 90f)] public float spotAngle = 35f;
    public bool requireLineOfSight = true;
    public LayerMask lineOfSightMask = ~0;

    [Header("Discovery")]
    [SerializeField] private float requiredFocusTime = 2f;
    [SerializeField] private int requiredScanPoints = 12;
    [SerializeField] private Material[] discoveredMaterials;

    [Header("LiDAR Mesh")]
    [SerializeField] private SkinnedMeshRenderer skinnedMeshRenderer;
    [SerializeField] private MeshCollider lidarMeshCollider;

    [Header("Combat camera")]
    public Transform focusPoint;
    public float focusHeight = 1.2f;

    [Header("Combat Animation")]
    public Animator animator;
    public float attackDuration = 1.5f;

    private Action attackFinished;
    private Coroutine attackCoroutine;

    private Mesh bakedMesh;
    private int lastBakeFrame = -1;
    private Vector3[] bakedVertices;
    private Vector3[] bakedNormals;

    private float focusTimer;
    private int registeredScanPoints;

    public RoomNode Room { get; set; }
    public bool Resolved { get; private set; }
    public bool Discovered { get; private set; }

    public int RegisteredScanPoints => registeredScanPoints;
    public float FocusTimer => focusTimer;

    public Vector3 FocusPosition => focusPoint != null ? focusPoint.position : transform.position + Vector3.up * focusHeight;


    private void Awake()
    {
        if (skinnedMeshRenderer == null)
            skinnedMeshRenderer = GetComponentInChildren<SkinnedMeshRenderer>(true);

        if (skinnedMeshRenderer == null)
        {
            Debug.LogError($"[Enemy] {name}: No SkinnedMeshRenderer found.");
            return;
        }

        if (lidarMeshCollider == null)
            lidarMeshCollider = skinnedMeshRenderer.GetComponent<MeshCollider>();

        if (lidarMeshCollider == null)
        {
            Debug.LogError($"[Enemy] {name}: No MeshCollider found on the SkinnedMeshRenderer GameObject.");
            return;
        }

        lidarMeshCollider.convex = false;
        lidarMeshCollider.isTrigger = false;

        bakedMesh = new Mesh
        {
            name = $"{name}_LiDAR_BakedMesh"
        };

        BakeAnimatedMesh();

        lidarMeshCollider.sharedMesh = null;
        lidarMeshCollider.sharedMesh = bakedMesh;

        Physics.SyncTransforms();

        Debug.Log($"[Enemy] {name}: LiDAR MeshCollider initialized.");
    }


    void LateUpdate()
    {
        BakeAnimatedMesh();

        if (lidarMeshCollider == null || bakedMesh == null)
            return;

        lidarMeshCollider.sharedMesh = null;
        lidarMeshCollider.sharedMesh = bakedMesh;
    }

    public bool IsSeenBy(Transform viewer)
    {
        if (Resolved || Discovered || viewer == null) return false;

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

    public void UpdateGaze(bool isFocused, float deltaTime)
    {
        if (Resolved || Discovered)
            return;

        if (isFocused)
            focusTimer += deltaTime;
        else
            focusTimer = 0f;

        CheckDiscovery();
    }

    public void RegisterScanPoint()
    {
        if (Resolved || Discovered)
            return;

        registeredScanPoints++;
        CheckDiscovery();
    }


    private void CheckDiscovery()
    {
        if (Discovered || Resolved)
            return;

        Debug.Log(
            $"[Enemy Discovery] {name} | " +
            $"Focus: {focusTimer:F2}/{requiredFocusTime} | " +
            $"Points: {registeredScanPoints}/{requiredScanPoints}"
        );

        if (focusTimer < requiredFocusTime ||
            registeredScanPoints < requiredScanPoints)
            return;

        Discovered = true;

        Debug.Log($"[Enemy Discovery] {name} DISCOVERED!");

        if (skinnedMeshRenderer != null &&
            discoveredMaterials != null &&
            discoveredMaterials.Length > 0)
        {
            skinnedMeshRenderer.sharedMaterials = discoveredMaterials;
        }
        else
        {
            Debug.LogWarning(
                $"[Enemy] {name}: Assign the visible materials in Discovered Materials."
            );
        }
        
        if (CombatController.Instance != null)
        {
            CombatController.Instance.NotifyEnemySpotted(this);
        }
        else
        {
            Debug.LogError("[Enemy Discovery] No CombatController instance found!");
        }
    }


    public bool TryGetLidarSurfaceHit(Ray ray, out RaycastHit hit)
    {
        hit = default;

        if (skinnedMeshRenderer == null ||
            lidarMeshCollider == null ||
            bakedMesh == null)
            return false;

        BakeAnimatedMesh();

        lidarMeshCollider.sharedMesh = null;
        lidarMeshCollider.sharedMesh = bakedMesh;

        Physics.SyncTransforms();

        return lidarMeshCollider.Raycast(ray, out hit, Mathf.Infinity);
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

    private void BakeAnimatedMesh()
    {
        if (bakedMesh == null || skinnedMeshRenderer == null)
            return;

        if (lastBakeFrame == Time.frameCount)
            return;

        skinnedMeshRenderer.BakeMesh(bakedMesh, true);

        bakedVertices = bakedMesh.vertices;
        bakedNormals = bakedMesh.normals;

        lastBakeFrame = Time.frameCount;
    }

    public SurfaceAttachment CreateSurfaceAttachment(RaycastHit hit)
    {
        return new SurfaceAttachment
        {
            TriangleIndex = hit.triangleIndex,
            BarycentricCoordinate = hit.barycentricCoordinate
        };
    }

    public bool GetAnimatedSurfacePoint(SurfaceAttachment attachment, out Vector3 worldPosition, out Vector3 worldNormal)
    {
        worldPosition = Vector3.zero;
        worldNormal = Vector3.up;

        if (skinnedMeshRenderer == null || bakedMesh == null)
            return false;

        BakeAnimatedMesh();

        int[] triangles = bakedMesh.triangles;
        int triangleOffset = attachment.TriangleIndex * 3;

        if (triangleOffset + 2 >= triangles.Length)
            return false;

        int i0 = triangles[triangleOffset];
        int i1 = triangles[triangleOffset + 1];
        int i2 = triangles[triangleOffset + 2];

        if (i0 >= bakedVertices.Length ||
            i1 >= bakedVertices.Length ||
            i2 >= bakedVertices.Length)
            return false;

        Vector3 bary = attachment.BarycentricCoordinate;

        Vector3 localPosition =
            bakedVertices[i0] * bary.x +
            bakedVertices[i1] * bary.y +
            bakedVertices[i2] * bary.z;

        Vector3 localNormal;

        if (bakedNormals != null &&
            bakedNormals.Length == bakedVertices.Length)
        {
            localNormal =
                bakedNormals[i0] * bary.x +
                bakedNormals[i1] * bary.y +
                bakedNormals[i2] * bary.z;
        }
        else
        {
            localNormal = Vector3.Cross(
                bakedVertices[i1] - bakedVertices[i0],
                bakedVertices[i2] - bakedVertices[i0]
            );
        }

        worldPosition = skinnedMeshRenderer.transform.TransformPoint(localPosition);

        worldNormal = skinnedMeshRenderer.transform.worldToLocalMatrix
            .transpose
            .MultiplyVector(localNormal)
            .normalized;

        return true;
    }

    private void OnDestroy()
    {
        if (bakedMesh != null)
            Destroy(bakedMesh);
    }
}