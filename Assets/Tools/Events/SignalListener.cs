using UnityEngine;
using UnityEngine.Events;

namespace Tools.Events
{
    public sealed class SignalListener : MonoBehaviour
    {
        // --- CONFIGURABLE FIELDS ---

        [SerializeField]
        private SignalChannel channel;
        [SerializeField]
        private UnityEvent response = new UnityEvent();

        // --- SUBSCRIPTION STATE ---

        SignalChannel subscribed;

        void OnEnable()
        {
            // Remember the channel so cleanup still works if the Inspector changes
            subscribed = channel;
            if (subscribed != null) subscribed.Raised += Respond;
        }

        void OnDisable()
        {
            // Stop listening while this component is inactive
            if (subscribed != null) subscribed.Raised -= Respond;
            subscribed = null;
        }

        void Respond() => response.Invoke();
    }
}
