using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// First VR combat UI: a world-space hand placed once when battle begins.
// Does not parent UI to the tracked head or rotate the player's view.
public sealed class VRCombatCards : MonoBehaviour
{
    CardSystem cards;
    CombatController combat;
    PlayerHealth health;
    Camera head;
    InputActionAsset inputs;
    InputAction choose, play;
    GameObject panel;
    Image[] frames;
    RawImage[] artwork;
    TMP_Text[] labels;
    TMP_Text status, details;
    bool wasInCombat, selectionArmed, playArmed;

    public void Initialize(CardSystem cardSystem, CombatController encounters, PlayerHealth playerHealth, Camera view)
    {
        cards = cardSystem; combat = encounters; health = playerHealth; head = view;
        inputs = ScriptableObject.CreateInstance<InputActionAsset>();
        var map = new InputActionMap("VRCombat");
        inputs.AddActionMap(map);
        choose = map.AddAction("Choose Card", InputActionType.Value, expectedControlLayout: "Vector2");
        choose.AddBinding("<XRController>{RightHand}/primary2DAxis");
        play = map.AddAction("Play Card", InputActionType.Button);
        play.AddBinding("<XRController>{RightHand}/trigger");
        inputs.Enable();
        CreatePanel();
    }

    void Update()
    {
        if (cards == null || combat == null || panel == null) return;
        bool inCombat = combat.CurrentState == CombatController.State.Combat && cards.CurrentMode == CardSystem.Mode.Combat;
        if (inCombat && !wasInCombat)
        {
            Vector3 forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            panel.transform.SetPositionAndRotation(head.transform.position + forward * 1.15f - Vector3.up * 0.35f,
                Quaternion.LookRotation(forward, Vector3.up));
            selectionArmed = playArmed = false;
        }
        wasInCombat = inCombat;
        panel.SetActive(inCombat);
        if (!inCombat) return;
        if (!PauseMenuController.BlocksGameplayInput && !health.IsDead)
        {
            float horizontal = choose.ReadValue<Vector2>().x;
            if (Mathf.Abs(horizontal) < 0.25f) selectionArmed = true;
            else if (selectionArmed && Mathf.Abs(horizontal) > 0.65f)
            {
                selectionArmed = false;
                cards.SelectRelativeCard(horizontal > 0 ? 1 : -1);
            }
            if (!play.IsPressed()) playArmed = true;
            if (playArmed && play.WasPressedThisFrame())
            {
                playArmed = false;
                cards.PlaySelectedCard();
            }
        }
        RefreshPanel();
    }

    void CreatePanel()
    {
        panel = new GameObject("VR Combat Hand", typeof(RectTransform), typeof(Canvas));
        panel.transform.SetParent(transform, false);
        var canvas = panel.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = head;
        panel.GetComponent<RectTransform>().sizeDelta = new Vector2(1000, 500);
        panel.transform.localScale = Vector3.one * 0.001f;
        status = Text("Combat Status", panel.transform, new Vector2(1000, 75), new Vector2(0, 195), 26);
        details = Text("Selected Card Details", panel.transform, new Vector2(950, 90), new Vector2(0, -205), 22);
        int count = Mathf.Clamp(cards.handSize, 1, 8);
        frames = new Image[count]; artwork = new RawImage[count]; labels = new TMP_Text[count];
        float width = 940f / count;
        for (int i = 0; i < count; i++)
        {
            var slot = Rect("Card " + (i + 1), panel.transform, new Vector2(width - 12, 280),
                new Vector2((i - (count - 1) * 0.5f) * width, 0));
            frames[i] = slot.gameObject.AddComponent<Image>();
            frames[i].raycastTarget = false;
            var art = Rect("Artwork", slot, new Vector2(width - 28, 210), new Vector2(0, 16));
            artwork[i] = art.gameObject.AddComponent<RawImage>();
            artwork[i].raycastTarget = false;
            var fit = art.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 0.7f;
            // Restrict the artwork's parent area so its fit never covers the label.
            var area = Rect("Artwork Area", slot, new Vector2(width - 28, 210), new Vector2(0, 16));
            art.SetParent(area, false);
            labels[i] = Text("Card Name", slot, new Vector2(width - 18, 52), new Vector2(0, -108), 20);
        }
        panel.SetActive(false);
    }

    void RefreshPanel()
    {
        status.text = (health.IsDead ? "Player defeated" : cards.IsPlayerTurn ? "Your turn" : "Enemy turn")
            + " | Health " + health.CurrentHealth + "/" + health.maxHealth + " | Shield " + health.CurrentShield
            + "\nRight stick: choose card     Right trigger: play";
        for (int i = 0; i < frames.Length; i++)
        {
            bool exists = i < cards.Hand.Count;
            frames[i].gameObject.SetActive(exists);
            if (!exists) continue;
            var card = cards.Hand[i];
            frames[i].color = i == cards.SelectedIndex ? new Color(0.1f, 0.75f, 0.65f, 0.95f) : new Color(0.035f, 0.04f, 0.05f, 0.94f);
            artwork[i].texture = card.Data != null ? card.Data.frontArt : null;
            artwork[i].enabled = artwork[i].texture != null;
            labels[i].text = card.Data != null ? card.Data.displayName + " · Level " + card.Level : "Unknown card";
        }
        details.text = cards.SelectedIndex >= 0 && cards.SelectedIndex < cards.Hand.Count && cards.Hand[cards.SelectedIndex].Data != null
            ? cards.Hand[cards.SelectedIndex].Data.description : "";
    }

    static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        rect.gameObject.layer = 5;
        return rect;
    }

    static TMP_Text Text(string name, Transform parent, Vector2 size, Vector2 position, float fontSize)
    {
        var text = Rect(name, parent, size, position).gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    void OnDestroy()
    {
        if (inputs != null) { inputs.Disable(); Destroy(inputs); }
    }
}
