using UnityEngine;

// Put this on your player (or camera). It tells every LiDAR material where
// the player is and how far the "scan" reaches.
[ExecuteAlways]
public class LiDAR_origin : MonoBehaviour
{
    public float range = 15f;

    static readonly int OriginId = Shader.PropertyToID("_LidarOrigin");
    static readonly int RangeId = Shader.PropertyToID("_LidarRange");

    void Update()
    {
        Shader.SetGlobalVector(OriginId, transform.position);
        Shader.SetGlobalFloat(RangeId, range);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, range);
    }
}