using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Purchasing;

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
    public float focusHeight = 0f;

    [Header("Combat Animation")]
    public Animator animator;
    public float attackDuration = 1.5f;

    [Header("Audio")]
    public AudioSource presenceAudioSource;
    public AudioSource attackAudioSource;

    public AudioClip presenceAudioClip;
    public AudioClip attackAudioClip;

    [Header("Health")]
    public int maxHealth = 10;
    private int currentHealth;
    public int CurrentHealth => currentHealth;
    public bool IsDead => currentHealth <= 0;

    [Header("Combat")]
    public int attackDamage = 3;

    private Action attackFinished;
    private Coroutine attackCoroutine;

    private Mesh bakedMesh;
    private int lastBakeFrame = -1;
    private Vector3[] bakedVertices;
    private Vector3[] bakedNormals;
    private int[] bakedTriangles;

    private float focusTimer;
    private int registeredScanPoints;

    public RoomNode Room { get; set; }
    public bool Resolved { get; private set; }
    public bool Discovered { get; private set; }

    public int RegisteredScanPoints => registeredScanPoints;
    public float FocusTimer => focusTimer;
    public int ScanTriangleCount => bakedTriangles != null ? bakedTriangles.Length / 3 : 0;
    string scanSurfaceSignature;

    [Header("Presence Audio Volume")]
    [SerializeField, Range(0f, 1f)]
    private float normalPresenceVolume = 0.05f;

    [SerializeField, Range(0f, 1f)]
    private float focusedPresenceVolume = 0.30f;

    [SerializeField]
    private float focusVolumeTransition = 0.4f;

    private Coroutine presenceVolumeRoutine;


    private DamageOverTimeEffect activeDamageOverTime;
    [System.Serializable]
    public class DamageOverTimeEffect
    {
        public int damagePerTurn;
        public int remainingTurns;
        public AudioClip soundVFX;
    }

    public string ScanSurfaceSignature
    {
        get
        {
            if (scanSurfaceSignature != null) return scanSurfaceSignature;
            if (bakedMesh == null) return "";
            // The baked mesh is CPU-readable even when the imported asset is not.
            // Topology is stable across animation poses; baked vertex positions are not.
            var mesh = bakedMesh;
            using (var bytes = new System.IO.MemoryStream())
            {
                using (var writer = new System.IO.BinaryWriter(bytes, System.Text.Encoding.UTF8, true))
                {
                    writer.Write(mesh.vertexCount);
                    foreach (int triangle in bakedTriangles) writer.Write(triangle);
                }
                using (var hash = System.Security.Cryptography.SHA256.Create())
                    scanSurfaceSignature = BitConverter.ToString(hash.ComputeHash(bytes.ToArray())).Replace("-", "");
            }
            return scanSurfaceSignature;
        }
    }

    public void RestoreScanProgress(int pointCount, float focusedSeconds, bool discovered)
    {
        registeredScanPoints = pointCount;
        focusTimer = focusedSeconds;
        Discovered = discovered;
        if (discovered && skinnedMeshRenderer != null && discoveredMaterials != null && discoveredMaterials.Length > 0)
            skinnedMeshRenderer.sharedMaterials = discoveredMaterials;
        // Do not notify combat merely because a save is being restored.
    }

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

        InitializeAudio();

        currentHealth = maxHealth;
    }

    private void InitializeAudio()
    {
        if (presenceAudioSource != null)
        {
            presenceAudioSource.clip = presenceAudioClip;
            presenceAudioSource.loop = true;
            presenceAudioSource.playOnAwake = false;
            presenceAudioSource.volume = normalPresenceVolume;
        }

        if (attackAudioSource != null)
            attackAudioSource.playOnAwake = false;
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
        {
            SetPresenceFocused(false);
            return;
        }

        SetPresenceFocused(isFocused);

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

        PlayAttackAudio();

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
        if (Resolved) return;

        Resolved = true;

        FindAnyObjectByType<LiDARScanner>()?.RemoveEnemyDots(this);

        StopPresenceAudio();

        if (attackAudioSource != null)
            attackAudioSource.Stop();

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
        if (bakedTriangles == null) bakedTriangles = bakedMesh.triangles;

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

        int[] triangles = bakedTriangles;
        if (attachment.TriangleIndex < 0 || attachment.TriangleIndex > (int.MaxValue - 2) / 3) return false;
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

    public void StartPresenceAudio()
    {
        if (Resolved || !gameObject.activeInHierarchy)
            return;

        if (presenceAudioSource == null || presenceAudioClip == null)
            return;

        presenceAudioSource.enabled = true;

        if (!presenceAudioSource.isPlaying)
        {
            Debug.Log($"[Enemy Audio] Starting presence audio for {name}");
            presenceAudioSource.Play();
        }
    }

    public void StopPresenceAudio()
    {
        if (presenceAudioSource == null)
            return;

        presenceAudioSource.Stop();
    }

    public void PlayAttackAudio()
    {
        if (attackAudioSource == null || attackAudioClip == null)
            return;

        attackAudioSource.PlayOneShot(attackAudioClip);
    }

    public virtual void TakeDamage(int damage)
    {
        if (Resolved || IsDead)
            return;

        if (damage <= 0)
            return;

        currentHealth -= damage;

        Debug.Log(
            $"[Enemy] {name} took {damage} damage. " +
            $"Health: {currentHealth}/{maxHealth}"
        );

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            Die();
        }
    }

    public void ApplyDamageOverTime(int damagePerTurn, int duration, AudioClip attackAudioClip)
    {
        if (Resolved || IsDead)
            return;

        if (damagePerTurn <= 0 || duration <= 0)
            return;

        activeDamageOverTime = new DamageOverTimeEffect
        {
            damagePerTurn = damagePerTurn,
            remainingTurns = duration,
            soundVFX = attackAudioClip,
        };

        Debug.Log(
            $"[Enemy] {name} recebeu Damage Over Time: " +
            $"{damagePerTurn} dano por {duration} turnos."
        );
    }

    public void ProcessDamageOverTime()
    {
        if (activeDamageOverTime == null)
            return;

        if (Resolved || IsDead)
        {
            activeDamageOverTime = null;
            return;
        }

        TakeDamage(activeDamageOverTime.damagePerTurn);

        activeDamageOverTime.remainingTurns--;

        AudioSource.PlayClipAtPoint(activeDamageOverTime.soundVFX, this.transform.position);

        Debug.Log(
            $"[Enemy] {name} sofreu {activeDamageOverTime.damagePerTurn} " +
            $"dano por Damage Over Time. " +
            $"Turnos restantes: {activeDamageOverTime.remainingTurns}"
        );

        if (activeDamageOverTime.remainingTurns <= 0)
        {
            activeDamageOverTime = null;

            Debug.Log(
                $"[Enemy] Damage Over Time terminou em {name}."
            );
        }
    }

    protected virtual void Die()
    {
        Debug.Log($"[Enemy] {name} died.");

        Resolve();
    }

    private void OnDestroy()
    {
        if (bakedMesh != null)
            Destroy(bakedMesh);
    }

    public void SetPresenceFocused(bool focused)
    {
        if (presenceAudioSource == null)
            return;

        if (presenceVolumeRoutine != null)
            StopCoroutine(presenceVolumeRoutine);

        float targetVolume = focused
            ? focusedPresenceVolume
            : normalPresenceVolume;

        presenceVolumeRoutine = StartCoroutine(
            ChangePresenceVolume(targetVolume)
        );
    }

    private IEnumerator ChangePresenceVolume(float targetVolume)
    {
        float startVolume = presenceAudioSource.volume;
        float elapsed = 0f;

        while (elapsed < focusVolumeTransition)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(
                elapsed / Mathf.Max(0.01f, focusVolumeTransition)
            );

            t = Mathf.SmoothStep(0f, 1f, t);

            presenceAudioSource.volume = Mathf.Lerp(
                startVolume,
                targetVolume,
                t
            );

            yield return null;
        }

        presenceAudioSource.volume = targetVolume;
        presenceVolumeRoutine = null;
    }
}
