using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Added automatically at runtime. Can also be added in the Inspector to override
// the sound on a particular button. No EventTrigger or drag handlers are needed.
[RequireComponent(typeof(Selectable))]
public class MenuSfxControl : MonoBehaviour, IPointerEnterHandler
{
    public enum ActivationSound { Automatic, Confirm, Back, Silent }
    [SerializeField] ActivationSound activation = ActivationSound.Automatic;
    public bool IsSilent => activation == ActivationSound.Silent;
    public bool AllowFocusWhenSilent { get; set; }

    Selectable control;
    Button button;
    Slider slider;
    Toggle toggle;
    TMP_Dropdown dropdown;
    Dropdown legacyDropdown;
    UnityAction clicked;
    float lastTick = -1f;

    void Awake()
    {
        control = GetComponent<Selectable>();
        button = GetComponent<Button>();
        slider = GetComponent<Slider>();
        toggle = GetComponent<Toggle>();
        dropdown = GetComponent<TMP_Dropdown>();
        legacyDropdown = GetComponent<Dropdown>();
        BindButtonSound();
        if (slider != null) slider.onValueChanged.AddListener(OnSlider);
        if (toggle != null) toggle.onValueChanged.AddListener(OnToggle);
        if (dropdown != null) dropdown.onValueChanged.AddListener(OnDropdown);
        if (legacyDropdown != null) legacyDropdown.onValueChanged.AddListener(OnDropdown);
    }

    public void SetActivationSound(ActivationSound value)
    {
        activation = value;
        BindButtonSound();
    }

    void BindButtonSound()
    {
        if (button == null) return; // Inactive book tabs may not have awakened yet.
        if (clicked != null) button.onClick.RemoveListener(clicked);
        // Keep a plain delegate so scene-changing callbacks remain safe.
        ActivationSound sound = activation;
        if (sound == ActivationSound.Automatic)
        {
            string label = name.ToLowerInvariant();
            sound = label.Contains("back") || label.Contains("close") || label.Contains("cancel")
                || label.Contains("resume") || label.Contains("stay")
                ? ActivationSound.Back : ActivationSound.Confirm;
        }
        clicked = sound == ActivationSound.Back ? MenuSfx.Back
            : sound == ActivationSound.Silent ? (UnityAction)(() => { }) : MenuSfx.Confirm;
        button.onClick.AddListener(clicked);
    }

    public void OnPointerEnter(PointerEventData data)
    {
        // Touch begins with a press, rather than mouse hover.
        if (data is ExtendedPointerEventData extended)
        {
            if (extended.pointerType == UIPointerType.Touch) return;
        }
        else if (data.pointerId >= 0) return;
        MenuSfx.Focus(control);
    }

    bool IsBeingUsed()
    {
        return control.IsActive() && control.IsInteractable()
            && EventSystem.current != null
            && EventSystem.current.currentSelectedGameObject == gameObject;
    }

    void OnSlider(float value)
    {
        if (!IsBeingUsed() || Time.unscaledTime - lastTick < 0.08f) return;
        lastTick = Time.unscaledTime;
        MenuSfx.Focus(control);
    }

    void OnToggle(bool value) { if (IsBeingUsed()) MenuSfx.Confirm(); }
    void OnDropdown(int value) { if (IsBeingUsed()) MenuSfx.Confirm(); }

    void OnDestroy()
    {
        if (button != null && clicked != null) button.onClick.RemoveListener(clicked);
        if (slider != null) slider.onValueChanged.RemoveListener(OnSlider);
        if (toggle != null) toggle.onValueChanged.RemoveListener(OnToggle);
        if (dropdown != null) dropdown.onValueChanged.RemoveListener(OnDropdown);
        if (legacyDropdown != null) legacyDropdown.onValueChanged.RemoveListener(OnDropdown);
    }
}
