using UnityEditor;
using UnityEngine;

// The source book is baked as one-hot vertex poses. Cubic tangents introduce
// negative weights between samples and stretch the turning leaf past the cover.
public class BestiaryBookAnimationImporter : AssetPostprocessor
{
    void OnPostprocessAnimation(GameObject root, AnimationClip clip)
    {
        if (assetPath != "Assets/Bestiary/Models/BestiaryBook.fbx") return;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (!binding.propertyName.StartsWith("blendShape.Pose")) continue;
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }
    }
}
