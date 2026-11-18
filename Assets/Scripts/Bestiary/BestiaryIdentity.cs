using UnityEngine;

// Put this beside Enemy on an enemy prefab. All instances share the same entry.
[DisallowMultipleComponent]
[RequireComponent(typeof(Enemy))]
public class BestiaryIdentity : MonoBehaviour
{
    public BestiaryEntry entry;
}
