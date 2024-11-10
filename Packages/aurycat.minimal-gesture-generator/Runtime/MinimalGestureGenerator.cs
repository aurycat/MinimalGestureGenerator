#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEditor.Animations;

namespace aurycat.MGG
{

// This class just stores the data entered in the inspector.
// Look to MGGEditor.cs or MGGMain.cs for all the real work.
public class MinimalGestureGenerator : MonoBehaviour
{
    public Animator AvatarRoot;

    // It's called "fx" because its normally the avatar's FX controller,
    // but it can really be anything.
    public AnimatorController FXController;
    public const string DefaultLayerName = "MGG__Expressions";
    public string LayerName;

    public AnimatorController AssetContainer;
    public string AssetKeyName;

    public MGGUtil.StandardGestureFlag EnabledGestures = MGGUtil.StandardGestureFlagAll;
    public MGGUtil.ComboGestureMode ComboGestureMode = MGGUtil.ComboGestureMode.Symmetric;

    public Motion[] HandGestureExpressions = new Motion[MGGUtil.NumAsymmetricComboGestures]; // Fixed length
    public ContactExpressionInfo[] ContactExpressions; // Variable length
    public Motion[] ManualOverrideExpressions; // Variable length
    public Motion[] EyesClosedMotions; // Variable length

    public float TransitionDuration = 0.2f;
    public bool TransitionInterruption = false;
    public bool UseWriteDefaults = false;
    public AvatarMask Mask = null;

    public bool UseContactExpressions = false;
    public bool UseManualOverrideExpressions = false;
    public bool UsePauseHandGestures = false;
    public bool UsePauseContactExpressions = false;

    public const string DefaultPauseHandGesturesParamName = "MGG/PauseHandGestures";
    public const string DefaultPauseContactExpressionsParamName = "MGG/PauseContactExpressions";
    public const string DefaultManualOverrideParamName = "MGG/ManualOverride";

    public string PauseHandGesturesParamName = DefaultPauseHandGesturesParamName;
    public string PauseContactExpressionsParamName = DefaultPauseContactExpressionsParamName;
    public string ManualOverrideParamName = DefaultManualOverrideParamName;

    public bool UseIsLocalCheck = false;
    public AnimationClip IsLocalClip = null;
    public AnimationClip IsRemoteClip = null;

    void OnValidate()
    {
        AssetKeyName = (AssetKeyName == null) ? "" : AssetKeyName.Trim();
        LayerName = (LayerName == null) ? "" : LayerName.Trim();
    }
}

// Note this should be a struct, not a class, otherwise the code for
// PrepareContactExpressionMotions will need to change a bit to avoid
// modifying the source ContactExpressions array when creating blend trees.
[Serializable]
public struct ContactExpressionInfo {
    public Motion motion;
    public string paramName;
    // Define these two bools as "negatives" so that the default value
    // of False means AutoBlend/HighPriority are the defaults
    public bool disableAutoBlend;
    public bool lowPriority;
}

}
#endif