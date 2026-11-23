using Tools.Georeferencing;
using UnityEngine;

[CreateAssetMenu(menuName = "Bestiary/Enemy Entry", fileName = "EnemyEntry")]
public class BestiaryEntry : ScriptableObject
{
    [Tooltip("Permanent unique ID, e.g. enemy.snake. Keep this unchanged once discovery has been saved.")]
    public string enemyId;
    public string displayName;
    [TextArea(3, 8)] public string description;

    [Header("Appearance (optional until artwork is ready)")]
    public Sprite silhouette;
    public Sprite portrait;

    [Header("3D preview (optional; portrait is the fallback)")]
    public GameObject modelPrefab;
    public Vector3 previewEulerAngles;
    [Min(1f)] public float previewPadding = 1.2f;

    [Header("Possible drops (information only, not a gameplay drop table)")]
    [Tooltip("False means unknown. True with an empty list means this enemy drops no cards.")]
    public bool dropInformationKnown;
    public CardData[] possibleDrops = new CardData[0];

    [Header("Enemy regions (not the player's current GPS location)")]
    [Tooltip("Reference the existing georeferencing region assets. Empty means not yet assigned.")]
    public ContentRegion[] regions = new ContentRegion[0];
}
