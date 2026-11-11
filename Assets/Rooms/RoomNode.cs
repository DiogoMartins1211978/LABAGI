using UnityEngine;

public enum MoveDirection { Forward, Back, Left, Right }

public class RoomNode : MonoBehaviour
{
    [Header("Room size (meters)")]
    public float width = 8f;
    public float depth = 12f;

    [Header("Player stand point")]
    [Tooltip("0.5 = centered (50/50 left/right)")]
    [Range(0f, 1f)] public float widthSplit = 0.5f;
    [Tooltip("Fraction of the room depth that lies BEHIND the player. 0.4 = 40% behind / 60% ahead. Set 0.6 for the opposite.")]
    [Range(0f, 1f)] public float depthBehind = 0.4f;

    [Header("Neighbors (empty = can't move that way)")]
    public RoomNode forward;
    public RoomNode back;
    public RoomNode left;
    public RoomNode right;

    [Header("Scan")]
    [Tooltip("LiDAR range used while the player is in this room")]
    public float scanRange = 12f;

    // Local position of the stand point (floor level)
    Vector3 LocalAnchor => new Vector3(width * (widthSplit - 0.5f), 0f, depth * (depthBehind - 0.5f));

    public Vector3 AnchorPosition => transform.TransformPoint(LocalAnchor);
    public Quaternion AnchorRotation => transform.rotation;

    public RoomNode GetNeighbor(MoveDirection dir)
    {
        switch (dir)
        {
            case MoveDirection.Forward: return forward;
            case MoveDirection.Back:    return back;
            case MoveDirection.Left:    return left;
            case MoveDirection.Right:   return right;
        }
        return null;
    }

    void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.2f, 1f, 0.6f, 0.6f);
        Gizmos.DrawWireCube(new Vector3(0f, 1.5f, 0f), new Vector3(width, 3f, depth));

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(LocalAnchor + Vector3.up * 0.1f, 0.15f);
        Gizmos.DrawLine(LocalAnchor + Vector3.up * 0.1f, LocalAnchor + Vector3.up * 0.1f + Vector3.forward * 1.5f);

        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = Color.cyan;
        DrawLink(forward);
        DrawLink(back);
        DrawLink(left);
        DrawLink(right);
    }

    void DrawLink(RoomNode other)
    {
        if (other != null)
            Gizmos.DrawLine(AnchorPosition + Vector3.up * 0.1f, other.AnchorPosition + Vector3.up * 0.1f);
    }
}
