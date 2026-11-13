using System;
using UnityEngine;

namespace Tools.Events
{
    [CreateAssetMenu(menuName = "Events/Signal")]
    public sealed class SignalChannel : ScriptableObject
    {
        // --- EVENT ---

        public event Action Raised;

        // Notify the listeners currently connected to this asset
        public void Raise() => Raised?.Invoke();
    }
}
