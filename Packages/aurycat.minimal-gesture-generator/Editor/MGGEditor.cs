using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System;
using System.Linq;
using ReorderableList = UnityEditorInternal.ReorderableList;

namespace aurycat.MGG
{

[CustomEditor(typeof(MinimalGestureGenerator))]
[CanEditMultipleObjects]
public class MGGEditor : Editor
{
    SerializedProperty avatarRootProp;
    SerializedProperty fxControllerProp;
    SerializedProperty assetContainerProp;
    SerializedProperty comboGestureModeProp;
    SerializedProperty enabledGesturesProp;
    SerializedProperty handGesturesProp;
    SerializedProperty contactExpressionsProp;
    SerializedProperty manualOverrideExpressionsProp;
    SerializedProperty eyesClosedMotionsProp;
    SerializedProperty transitionDurationProp;
    SerializedProperty useWriteDefaultsProp;
    SerializedProperty maskProp;
    SerializedProperty useContactExpressionsProp;
    SerializedProperty useManualOverrideExpressionsProp;
    SerializedProperty usePauseHandGesturesProp;
    SerializedProperty usePauseContactExpressionsProp;
    SerializedProperty pauseHandGesturesParamNameProp;
    SerializedProperty pauseContactExpressionsParamNameProp;
    SerializedProperty manualOverrideParamNameProp;

    CustomLayoutArrayFoldout contactGesturesList;
    CustomLayoutArrayFoldout manualOverrideGesturesList;
    CustomLayoutArrayFoldout eyesClosedMotionsList;

    GUIContent handGesturesListHeaderLabel;
    GUIContent neutralExpressionLabel;
    GUIContent enabledHandGesturesLabel;
    GUIContent createNewBlendTreeLabel;
    GUIContent clearAllHandGestureMotionsLabel;
    GUIContent writeDefaultsInfoLinkLabel;
    GUIContent dbtInfoLinkLabel;
    GUIContent installHaiVisualExpressionsEditorLabel;
    GUIContent openHaiVisualExpressionsEditorLabel;
    GUIContent autoBlendIcon;
    GUIContent highPriorityIcon;

    const string writeDefaultsInfoURL = "https://notes.sleightly.dev/write-defaults";
    const string dbtInfoURL = "https://notes.sleightly.dev/dbt-combining";
    const string haiVisualExpressionsEditorURL = "https://docs.hai-vr.dev/docs/products/visual-expressions-editor";

    Type visualExpressionsEditorType;

    static bool firstEnable = true;
    bool blendTreeMoreInfoExpanded = false;

    void OnEnable()
    {
        serializedObject.maxArraySizeForMultiEditing = MGGUtil.NumAsymmetricComboGestures;
        avatarRootProp = serializedObject.FindProperty("AvatarRoot");
        fxControllerProp = serializedObject.FindProperty("FXController");
        assetContainerProp = serializedObject.FindProperty("AssetContainer");
        comboGestureModeProp = serializedObject.FindProperty("ComboGestureMode");
        enabledGesturesProp = serializedObject.FindProperty("EnabledGestures");
        handGesturesProp = serializedObject.FindProperty("HandGestureExpressions");
        contactExpressionsProp = serializedObject.FindProperty("ContactExpressions");
        manualOverrideExpressionsProp = serializedObject.FindProperty("ManualOverrideExpressions");
        eyesClosedMotionsProp = serializedObject.FindProperty("EyesClosedMotions");
        transitionDurationProp = serializedObject.FindProperty("TransitionDuration");
        useWriteDefaultsProp = serializedObject.FindProperty("UseWriteDefaults");
        maskProp = serializedObject.FindProperty("Mask");
        useContactExpressionsProp = serializedObject.FindProperty("UseContactExpressions");
        useManualOverrideExpressionsProp = serializedObject.FindProperty("UseManualOverrideExpressions");
        usePauseHandGesturesProp = serializedObject.FindProperty("UsePauseHandGestures");
        usePauseContactExpressionsProp = serializedObject.FindProperty("UsePauseContactExpressions");
        pauseHandGesturesParamNameProp = serializedObject.FindProperty("PauseHandGesturesParamName");
        pauseContactExpressionsParamNameProp = serializedObject.FindProperty("PauseContactExpressionsParamName");
        manualOverrideParamNameProp = serializedObject.FindProperty("ManualOverrideParamName");

        handGesturesListHeaderLabel = new GUIContent("Hand Gesture Expressions");
        neutralExpressionLabel = new GUIContent("Neutral Expression");
        enabledHandGesturesLabel = new GUIContent("Enabled Hand Gestures");
        createNewBlendTreeLabel = new GUIContent("Create new BlendTree asset");
        clearAllHandGestureMotionsLabel = new GUIContent("Clear All Hand Gesture Motions");
        writeDefaultsInfoLinkLabel = new GUIContent($"Visit {writeDefaultsInfoURL} for more info about Write Defaults");
        dbtInfoLinkLabel = new GUIContent($"Visit {dbtInfoURL} for more info about Direct Blend Trees");
        installHaiVisualExpressionsEditorLabel = new GUIContent("Install Haï~ Visual Expressions Editor");
        openHaiVisualExpressionsEditorLabel = new GUIContent("Open Haï~ Visual Expressions Editor");
        autoBlendIcon = new GUIContent(EditorGUIUtility.IconContent("BlendTree Icon").image,
"Toggle on to automatically blend between Neutral and the provided AnimationClip " +
"using the float parameter as a percentage. Alternatively, provide your own " +
"BlendTree Motion to customize the blending behavior.");
        highPriorityIcon = new GUIContent(EditorGUIUtility.IconContent("UpArrow").image,
"Toggle on to make this contact expression have higher priority than hand gestures. " +
"If this is off, using any hand gesture will override this contact expression.");

        visualExpressionsEditorType = Type.GetType("Hai.VisualExpressionsEditor.Scripts.Editor.VisualExpressionsEditorWindow, VisualExpressionsEditor.Editor");

        EditorApplication.contextualPropertyMenu += OnPropertyContextMenu;

        if (firstEnable) {
            handGesturesProp.isExpanded = false;
            contactExpressionsProp.isExpanded = false;
            manualOverrideExpressionsProp.isExpanded = false;
            eyesClosedMotionsProp.isExpanded = false;
            firstEnable = false;
        }

        InitContactExpressionsArrayFoldout();
        InitManualOverridesArrayFoldout();
        InitEyesClosedArrayFoldout();
    }

    void OnDestroy()
    {
        EditorApplication.contextualPropertyMenu -= OnPropertyContextMenu;
    }

    [MenuItem("Tools/Add MinimalGestureGenerator Object")]
    static void AddMGGObject()
    {
        GameObject obj = new GameObject("MGG");
        Undo.RegisterCreatedObjectUndo(obj, "Create MGG Object");
        Undo.RegisterCompleteObjectUndo(obj, "Create MGG Object");
        obj.AddComponent(typeof(MinimalGestureGenerator));
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        bool clearAllHandGestureMotions = false;

        Animator avatarRoot = null;
        bool noAvatarRoot = false;
        if (!avatarRootProp.hasMultipleDifferentValues) {
            avatarRoot = avatarRootProp.objectReferenceValue as Animator;
            noAvatarRoot = (avatarRoot == null);
        }

        AnimatorController fxController = null;
        bool noFXController = false;
        if (!fxControllerProp.hasMultipleDifferentValues) {
            fxController = fxControllerProp.objectReferenceValue as AnimatorController;
            noFXController = (fxController == null);
        }

        bool noAssetContainer = !fxControllerProp.hasMultipleDifferentValues && assetContainerProp.objectReferenceValue == null;

        MGGUtil.ComboGestureMode comboMode;
        if (comboGestureModeProp.hasMultipleDifferentValues) {
            comboMode = MGGUtil.ComboGestureMode.Asymmetric;
        } else {
            comboMode = (MGGUtil.ComboGestureMode)comboGestureModeProp.enumValueIndex;
        }

        MGGUtil.StandardGestureFlag enabledGesturesMask;
        bool noEnabledGestures = false;
        if (enabledGesturesProp.hasMultipleDifferentValues) {
            enabledGesturesMask = MGGUtil.StandardGestureFlagAll;
        } else {
            enabledGesturesMask = (MGGUtil.StandardGestureFlag)enabledGesturesProp.enumValueFlag;
            enabledGesturesMask &= (~MGGUtil.StandardGestureFlagNeutral);
            noEnabledGestures = (enabledGesturesMask == MGGUtil.StandardGestureFlagNone);
        }

        bool noExpressions = false;
        if (noEnabledGestures) {
            if (contactExpressionsProp.hasMultipleDifferentValues ||
                manualOverrideExpressionsProp.hasMultipleDifferentValues) {
                bool noContactExpressions =
                    !contactExpressionsProp.hasMultipleDifferentValues &&
                    contactExpressionsProp.arraySize == 0;
                bool noManualOverrideExpressions =
                    !manualOverrideExpressionsProp.hasMultipleDifferentValues &&
                    manualOverrideExpressionsProp.arraySize == 0;
                noExpressions =
                    noEnabledGestures && noContactExpressions && noManualOverrideExpressions;
            }
            else {
                noExpressions = HasNoSetExpressions(target as MinimalGestureGenerator);
            }
        }

        EditorGUILayout.Space(15);

        {
            bool disableGenerate =
                noAvatarRoot || noFXController;

            GUIStyle backupWarningLabel = new GUIStyle();
            backupWarningLabel.fontStyle = FontStyle.Bold;
            backupWarningLabel.normal.textColor = Color.red;

            using (new EditorGUI.DisabledScope(disableGenerate)) {
                if (GUILayout.Button($"\nGenerate Animator Layer\n")) {
                    GeneratePressed();
                }
                GUILayout.Label("MAKE BACKUPS! This tool will modify the FX Controller.", backupWarningLabel);
            }

            if (!disableGenerate && noExpressions) {
                EditorGUILayout.HelpBox("No hand gestures, contact expressions, or override expressions are set. An empty animator will be generated.", MessageType.Warning);
            }
        }

        EditorGUILayout.Space(15);

        EditorGUILayout.BeginVertical("box");
        {
            EditorGUILayout.Space(10);
            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(avatarRootProp);
            if (noAvatarRoot) {
                EditorGUILayout.HelpBox("No Avatar Root is set. This should be the top-level GameObject of your avatar -- where the VRCAvatarDescriptor is.", MessageType.Error);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.PropertyField(fxControllerProp);
            if (noFXController) {
                EditorGUILayout.HelpBox("No FX Controller is set. This should usually be set to the FX Playable Layer controller of your avatar, but it can be any Animator Controller.", MessageType.Error);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.PropertyField(assetContainerProp);
            if (noAssetContainer) {
                EditorGUILayout.HelpBox("Leave this blank to automatically create an asset container the first time you press Generate Gestures.", MessageType.Info);
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(10);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(15);

        EditorGUILayout.BeginVertical("box");
        {
            EditorGUILayout.Space(10);
            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField("Hand Gesture Expressions", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.PropertyField(comboGestureModeProp);
            if (comboGestureModeProp.hasMultipleDifferentValues) {
                EditorGUILayout.HelpBox("Selected objects have different Combo Gesture Modes. Showing all gestures.", MessageType.Warning);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.PropertyField(enabledGesturesProp, enabledHandGesturesLabel);
            if (enabledGesturesProp.hasMultipleDifferentValues) {
                EditorGUILayout.HelpBox("Selected objects have different sets of enabled gestures. Showing all gestures.", MessageType.Warning);
            }
            else if (noEnabledGestures) {
                EditorGUILayout.HelpBox("All hand gestures are disabled.", MessageType.Warning);
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.PropertyField(usePauseHandGesturesProp,
                new GUIContent("Use 'Pause Hand Gestures' Parameter",
"If set, the generated animator will use a boolean parameter with the name given " +
"below to pause or unpause all hand gestures. When hand gestures are paused, the " +
"expression will always be Neutral unless a Contact Expression or Manual Override is active."));
            EditorGUI.indentLevel++;
            using (new EditorGUI.DisabledScope(!usePauseHandGesturesProp.boolValue && !usePauseHandGesturesProp.hasMultipleDifferentValues)) {
                EditorGUILayout.PropertyField(pauseHandGesturesParamNameProp,
                    new GUIContent("Param Name (Bool)", "Name of the boolean parameter for Pause Hand Gestures"));
            }
            if (usePauseHandGesturesProp.boolValue && !noEnabledGestures) {
                EditorGUILayout.HelpBox(
"Remember to add this parameter to your avatar's VRC synced parameter list!", MessageType.Info);
            }
            EditorGUI.indentLevel--;

            if (!noEnabledGestures) {
                EditorGUILayout.Space(10);

                EditorGUI.indentLevel++;
                handGesturesProp.isExpanded = CustomLayoutArrayFoldout.DrawArrayFoldoutHeader(
                    handGesturesProp.isExpanded, handGesturesListHeaderLabel, null, null);
                if (handGesturesProp.isExpanded) {
                    handGesturesProp.arraySize = MGGUtil.NumAsymmetricComboGestures;
                    if (comboMode == MGGUtil.ComboGestureMode.Asymmetric) {
                        var enumerator = handGesturesProp.GetEnumerator();
                        for (int i = 0; enumerator.MoveNext(); i++) {
                            if (i == 0) { continue; } // Skip neutral
                            if (!MGGUtil.GestureIsEnabled(MGGUtil.AsymmetricComboGestureUses[i], enabledGesturesMask)) {
                                continue;
                            }
                            SerializedProperty elemProp = enumerator.Current as SerializedProperty;
                            EditorGUILayout.PropertyField(elemProp, new GUIContent(MGGUtil.AsymmetricComboGestureNames[i]));
                        }
                    }
                    else if (comboMode == MGGUtil.ComboGestureMode.Symmetric) {
                        for (int i = 0; i < MGGUtil.NumSymmetricComboGestures; i++) {
                            if (i == 0) { continue; } // Skip neutral
                            if (!MGGUtil.GestureIsEnabled(MGGUtil.SymmetricComboGestureUses[i], enabledGesturesMask)) {
                                continue;
                            }
                            int motionArrayIndex = MGGUtil.SymmetricToAsymmetricMap[i];
                            SerializedProperty elemProp = handGesturesProp.GetArrayElementAtIndex(motionArrayIndex);
                            EditorGUILayout.PropertyField(elemProp, new GUIContent(MGGUtil.SymmetricComboGestureNames[i]));
                        }
                    }
                    else if (comboMode == MGGUtil.ComboGestureMode.SymmetricWithDoubles) {
                        for (int i = 0; i < MGGUtil.NumSymmetricDoublesComboGestures; i++) {
                            if (i == 0) { continue; } // Skip neutral
                            if (!MGGUtil.GestureIsEnabled(MGGUtil.SymmetricDoublesComboGestureUses[i], enabledGesturesMask)) {
                                continue;
                            }
                            int motionArrayIndex = MGGUtil.SymmetricDoublesToAsymmetricMap[i];
                            SerializedProperty elemProp = handGesturesProp.GetArrayElementAtIndex(motionArrayIndex);
                            EditorGUILayout.PropertyField(elemProp, new GUIContent(MGGUtil.SymmetricDoublesComboGestureNames[i]));
                        }
                    }

                    EditorGUILayout.Space(4);
                    if (IndentedButton(clearAllHandGestureMotionsLabel)) {
                        clearAllHandGestureMotions = true;
                    }
                }
                EditorGUI.indentLevel--;
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(10);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(15);

        EditorGUILayout.BeginVertical("box");
        {
            EditorGUILayout.Space(10);
            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField("Contact Expressions", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.HelpBox(
"Each Contact Expression uses a float parameter, specified in the array below. " +
"The intention is for these to correspond to VRC Contact Receivers, but they " +
"can technically be any float parameter. If the parameter value is greater than " +
"0, the Expression becomes active. The animator will blend between Neutral when " +
"the parameter is at 0 and the given AnimationClip when the parameter is at value " +
"1. Only one Contact Expression can be active a time, and they are selected in " +
"top-to-bottom order.", MessageType.Info);

            EditorGUILayout.Space(10);
            EditorGUILayout.PropertyField(useContactExpressionsProp);

            using (new EditorGUI.DisabledScope(!useContactExpressionsProp.boolValue && !useContactExpressionsProp.hasMultipleDifferentValues)) {
                EditorGUILayout.Space(10);
                EditorGUILayout.PropertyField(usePauseContactExpressionsProp,
                    new GUIContent("Use 'Pause Contact Expressions' Parameter",
    "If set, the generated animator will use a boolean parameter with the name given " +
    "below to pause or unpause all contact expressions. All contact expressions will " +
    "be ignored when the parameter's value is True."));
                EditorGUI.indentLevel++;
                using (new EditorGUI.DisabledScope(!usePauseContactExpressionsProp.boolValue && !usePauseContactExpressionsProp.hasMultipleDifferentValues)) {
                    EditorGUILayout.PropertyField(pauseContactExpressionsParamNameProp,
                        new GUIContent("Param Name (Bool)"));
                }
                if (usePauseContactExpressionsProp.boolValue && contactExpressionsProp.arraySize > 0) {
                    EditorGUILayout.HelpBox(
    "Remember to add this parameter to your avatar's VRC synced parameter list!", MessageType.Info);
                }
                EditorGUI.indentLevel--;

                EditorGUILayout.Space(10);
                EditorGUI.indentLevel++;
                contactGesturesList.Draw();
                EditorGUI.indentLevel--;
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(10);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(15);

        EditorGUILayout.BeginVertical("box");
        {
            EditorGUILayout.Space(10);
            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField("Manual Override Expressions", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.HelpBox(
$"Manual Expressions use a single shared integer property with the name given " +
"below. If the property value is 0, normal hand/contact gestures are used. If " +
"the property value is not 0, the corresponding element from the array below is " +
"used. These override all other hand gesture or contact expressions.", MessageType.Info);

            EditorGUILayout.Space(10);
            EditorGUILayout.PropertyField(useManualOverrideExpressionsProp);

            using (new EditorGUI.DisabledScope(!useManualOverrideExpressionsProp.boolValue && !useManualOverrideExpressionsProp.hasMultipleDifferentValues)) {
                EditorGUILayout.Space(10);
                EditorGUILayout.PropertyField(manualOverrideParamNameProp,
                    new GUIContent("Manual Override Param Name (Int)"));
                if (manualOverrideExpressionsProp.arraySize > 0) {
                    EditorGUILayout.HelpBox(
    "Remember to add this parameter to your avatar's VRC synced parameter list!", MessageType.Info);
                }

                EditorGUILayout.Space(10);
                EditorGUI.indentLevel++;
                manualOverrideGesturesList.Draw();
                EditorGUI.indentLevel--;
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(10);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(15);

        EditorGUILayout.BeginVertical("box");
        {
            EditorGUILayout.Space(10);
            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField("Disable Blinking For These Motions", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.HelpBox(
"It is best to add any AnimationClips/BlendTrees which have *both eyes closed*. " +
"Pausing blinking on clips with only one eye closed will cause the open eye to " +
"not have any eyelook movement.", MessageType.Info);

            EditorGUILayout.Space(10);
            EditorGUI.indentLevel++;
            eyesClosedMotionsList.Draw();
            EditorGUI.indentLevel--;

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(10);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(15);

        EditorGUILayout.BeginVertical("box");
        {
            EditorGUILayout.Space(10);
            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.PropertyField(transitionDurationProp,
                new GUIContent("Transition Duration (seconds)",
"How fast to blend between expressions, e.g. when you change your hand gesture. " +
" 0 will make the transition instant (will look unnatural!)"));

            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(maskProp,
                new GUIContent("Layer Mask",
"The AvatarMask to put on the generated layer. If unspecified, a mask will be " +
"generated automatically which allows all transforms that get animated by the " +
"provided AnimationClips, and denies all humanoid animations."));

            // EditorStyles.foldout seems to not work if used in OnEnable or the first
            // time OnInspectorGUI is called, so just recreate it every time...
            GUIStyle boldFoldoutStyle = new GUIStyle(EditorStyles.foldout);
            boldFoldoutStyle.fontStyle = FontStyle.Bold;

            EditorGUILayout.Space(10);
            // Just using a random property here for the isExpanded, since "Advanced"
            // itself isn't a property
            transitionDurationProp.isExpanded =
                EditorGUILayout.Foldout(transitionDurationProp.isExpanded, "Advanced", true, boldFoldoutStyle);
            if (transitionDurationProp.isExpanded) {
                EditorGUILayout.Space(4);
                EditorGUI.indentLevel++;

                SerializedProperty neutralMotionProp = handGesturesProp.GetArrayElementAtIndex(0);
                EditorGUILayout.PropertyField(neutralMotionProp, neutralExpressionLabel);
                EditorGUILayout.HelpBox(
"Leave this blank to use your avatar's default state as the neutral expression. " +
"Only fill this in if you want your avatar's neutral expression to NOT look " +
"like what you see in the scene view.", MessageType.Info);

                EditorGUILayout.Space(10);
                EditorGUILayout.PropertyField(useWriteDefaultsProp);
                EditorGUILayout.HelpBox(
"Generate animator states with Write Defaults On." +
"\n\n" +
"IMPORTANT: It is best practice to use Write Defaults Off! The states generated " +
"by MGG are compatible with WD On or WD Off animators, however, if your " +
"animator already uses WD On states, it may be useful to enable this to make " +
"AV3 Manager not complain about mixed WD usage. Additionally, using WD On may " +
"be necessary in some cases when using Direct Blend Trees.", MessageType.Info);
                if (IndentedLinkButton(writeDefaultsInfoLinkLabel)) {
                    Application.OpenURL(writeDefaultsInfoURL);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(10);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(15);

        EditorGUILayout.BeginVertical("box");
        {
            EditorGUILayout.Space(10);
            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField("Utilities", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            if (visualExpressionsEditorType == null) {
                if (IndentedButton(installHaiVisualExpressionsEditorLabel)) {
                    Application.OpenURL(haiVisualExpressionsEditorURL);
                }
            }
            else {
                if (IndentedButton(openHaiVisualExpressionsEditorLabel)) {
                    var show = visualExpressionsEditorType.GetMethod("ShowWindow");
                    if (show != null) {
                        show.Invoke(null, null);
                    }
                    else {
                        Debug.LogError("Error opening automatically. Open via  Window > Haï > VisualExpressionsEditor  instead.");
                    }
                }
            }
            EditorGUILayout.HelpBox(
"Haï~'s Visual Expressions Editor is very useful for creating AnimationClips from " +
"a combination of blend shapes. I highly recommend using it alongside MGG.", MessageType.Info);

            EditorGUILayout.Space(10);
            if (IndentedButton(createNewBlendTreeLabel)) {
                string path = AssetDatabase.GenerateUniqueAssetPath("Assets/New BlendTree.asset");
                BlendTree bt = new BlendTree();
                AssetDatabase.CreateAsset(bt, path);
                EditorGUIUtility.PingObject(bt);
            }
            EditorGUILayout.HelpBox(
"BlendTree assets can be used in place of AnimationClips above for more complex " +
"expressions such as face puppets.", MessageType.Info);

            blendTreeMoreInfoExpanded =
                EditorGUILayout.Foldout(blendTreeMoreInfoExpanded, "Warning about Direct Blend Trees", true);
            if (blendTreeMoreInfoExpanded) {
                EditorGUILayout.HelpBox(
"Direct Blend Trees (that is, Blend Trees using the 'Direct' blending type) should " +
"always use Write Defaults On, and can behave unexpectedly otherwise. However, by " +
"default, MGG generates animator states with Write Defaults Off, since that is " +
"the VRC-recommended practice. The upside is that MGG automatically converts " +
"input AnimationClips into versions that always contain every animated property used " +
"by all expressions, and that behavior alleviates one of the major issues with Write " +
"Defaults Off Direct Blend Trees. However, there may still be issues if other " +
"Blend Trees on other layers use the same parameters. If you choose to use Direct " +
"Blend Trees with MGG, be on the lookout for unintended side effects. See " +
"the links below for more info.", MessageType.Info);
                if (IndentedLinkButton(writeDefaultsInfoLinkLabel)) {
                    Application.OpenURL(writeDefaultsInfoURL);
                }
                if (IndentedLinkButton(dbtInfoLinkLabel)) {
                    Application.OpenURL(dbtInfoURL);
                }
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(10);
        }
        EditorGUILayout.EndVertical();

        serializedObject.ApplyModifiedProperties();

        bool changes = false;

        if (clearAllHandGestureMotions) {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            foreach (MinimalGestureGenerator t in targets) {
                Undo.RecordObject(t, "Clear All Hand Gesture Motions");
                PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                for (int i = 0; i < t.HandGestureExpressions.Length; i++) {
                    t.HandGestureExpressions[i] = null;
                }
            }
            Undo.CollapseUndoOperations(group);
            Undo.SetCurrentGroupName("Clear All Hand Gesture Motions");
            changes = true;
        }

        if (changes) {
            serializedObject.Update();
        }
    }

    void OnPropertyContextMenu(GenericMenu menu, SerializedProperty property)
    {
        if (property == pauseHandGesturesParamNameProp ||
            property == pauseContactExpressionsParamNameProp ||
            property == manualOverrideParamNameProp) {

            menu.AddItem(new GUIContent("Reset to Default Parameter Name"), false, () =>
            {
                if (property == pauseHandGesturesParamNameProp) {
                    property.stringValue = MinimalGestureGenerator.DefaultPauseHandGesturesParamName;
                }
                else if (property == pauseContactExpressionsParamNameProp) {
                    property.stringValue = MinimalGestureGenerator.DefaultPauseContactExpressionsParamName;
                }
                else if (property == manualOverrideParamNameProp) {
                    property.stringValue = MinimalGestureGenerator.DefaultManualOverrideParamName;
                }
                property.serializedObject.ApplyModifiedProperties();
            });
        }
    }

    void GeneratePressed()
    {
        try {
            AssetDatabase.StartAssetEditing();

            foreach (MinimalGestureGenerator t in targets) {
                if (t.AvatarRoot == null) {
                    Debug.LogWarning($"Skipping generating for '{t.name}' because no Avatar Root is set.");
                    continue;
                }

                if (t.FXController == null) {
                    Debug.LogWarning($"Skipping generating for '{t.name}' because no FX Controller is set.");
                    continue;
                }

                if (t.AssetContainer == null) {
                    t.AssetContainer = MakeAssetContainer();
                }

                MGGMain.Generate(t, targets.Length > 1);
            }
        }
        finally {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        serializedObject.Update();
    }

    bool HasNoSetExpressions(MinimalGestureGenerator t)
    {
        bool noEnabledGestures =
            (t.EnabledGestures & (~MGGUtil.StandardGestureFlagNeutral)) == MGGUtil.StandardGestureFlagNone;
        bool noContactExpressions =
            t.ContactExpressions.Where(
                (ce) => ce.motion != null && !String.IsNullOrEmpty(ce.paramName)
            ).Count() == 0;
        bool noManualOverrideExpressions =
            t.ManualOverrideExpressions.Where(m => m != null).Count() == 0;
        return (noEnabledGestures && noContactExpressions && noManualOverrideExpressions);
    }

    static public AnimatorController sharedAssetContainer = null;

    AnimatorController MakeAssetContainer()
    {
        if (sharedAssetContainer != null) {
            return sharedAssetContainer;
        }

        string name = "zAutogenerated__MGGAssetContainer";
        string defaultPath = $"Assets/{name}.controller";
        UnityEngine.Object asset;

        string[] guids = AssetDatabase.FindAssets($"{name} t:AnimatorController");
        if (guids != null && guids.Length > 0) {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            if (!String.IsNullOrEmpty(path)) {
                asset = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (asset != null && asset is AnimatorController) {
                    sharedAssetContainer = asset as AnimatorController;
                    return sharedAssetContainer;
                }
            }
        }

        if (!String.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(defaultPath, AssetPathToGUIDOptions.OnlyExistingAssets))) {
            // Asset with the expected name exists, but it wasn't loaded successfully
            // in the previous check. Abort out
            throw new Exception("Unable to create asset container, container appears to already exist, but can't be loaded.");
        }

        sharedAssetContainer = new AnimatorController();
        sharedAssetContainer.name = name;
        AssetDatabase.CreateAsset(sharedAssetContainer, defaultPath);
        return sharedAssetContainer;
    }

    public bool IndentedButton(GUIContent content)
    {
        Rect r = EditorGUI.IndentedRect(GUILayoutUtility.GetRect(content, GUI.skin.button));
        return GUI.Button(r, content, GUI.skin.button);
    }

    public bool IndentedLinkButton(GUIContent content)
    {
        Rect r = GUILayoutUtility.GetRect(content, EditorStyles.linkLabel);
        Rect ir = EditorGUI.IndentedRect(r);
        ir.width = r.width;
        return EditorGUI.LinkButton(ir, content);
    }

    void InitContactExpressionsArrayFoldout()
    {
        contactGesturesList = new CustomLayoutArrayFoldout(serializedObject, contactExpressionsProp);
        contactGesturesList.list.headerHeight = 0;
        contactGesturesList.list.elementHeight = EditorGUIUtility.singleLineHeight;
        contactGesturesList.list.drawElementCallback =  
        (Rect rect, int index, bool isActive, bool isFocused) => {
            
            if (contactExpressionsProp == null ||
                contactExpressionsProp.arraySize <= index)
            {
                return;
            }

            SerializedProperty elementProp          = contactExpressionsProp.GetArrayElementAtIndex(index);
            SerializedProperty paramNameProp        = elementProp.FindPropertyRelative("paramName");
            SerializedProperty motionProp           = elementProp.FindPropertyRelative("motion");
            SerializedProperty disableAutoBlendProp = elementProp.FindPropertyRelative("disableAutoBlend");
            SerializedProperty lowPriorityProp      = elementProp.FindPropertyRelative("lowPriority");

            //  <------------------------------ rect ------------------------------->
            //  _____________________________________________________________________
            //  | ## [.......param name.......]  [.......  motion  .......] [H] [A] |
            //  |___________________________________________________________________|
            //     ^                                    high priority toggle ^   ^
            //   label                                         auto blend toggle /

            float slh = EditorGUIUtility.singleLineHeight;
            float wPad = 4;

            float lleftPad = 8;
            float lhPad = Mathf.Max(0, rect.height - slh) / 2;
            Rect lineRect = new Rect(rect.x + lleftPad, rect.y + lhPad, Mathf.Max(0, rect.width - lleftPad), slh);

            Rect labelRect = new Rect(lineRect.x, lineRect.y, 25, lineRect.height);

            float toggleWidth = slh;
            Rect abToggleRect = new Rect(lineRect.xMax     - wPad - toggleWidth, lineRect.y, toggleWidth, slh);
            Rect hpToggleRect = new Rect(abToggleRect.xMin - wPad - toggleWidth, lineRect.y, toggleWidth, slh);

            Rect remainingRect = new Rect(labelRect.xMax + wPad, lineRect.y, 0, slh);
            remainingRect.xMax = hpToggleRect.xMin - wPad;

            float halfRemaining = (remainingRect.width - wPad)/2;
            Rect paramNameRect = new Rect(remainingRect.x,           remainingRect.y, halfRemaining, slh);
            Rect motionRect    = new Rect(paramNameRect.xMax + wPad, remainingRect.y, halfRemaining, slh);

            // Make the label a "property" so that it has the nice context menu
            // for array and prefab stuff.
            EditorGUI.BeginProperty(new Rect(labelRect.x, labelRect.y, labelRect.width+wPad, labelRect.height), GUIContent.none, elementProp);
            // 1-based element number, just for reference
            EditorGUI.LabelField(labelRect, $"{index+1}");
            EditorGUI.EndProperty();

            // Parameter name textbox
            EditorGUI.PropertyField(paramNameRect, paramNameProp, GUIContent.none);
            // Placeholder text
            if (!paramNameProp.hasMultipleDifferentValues && String.IsNullOrEmpty(paramNameProp.stringValue)) {
                GUIStyle placeholderTextStyle = new GUIStyle(GUI.skin.label);
                placeholderTextStyle.fontStyle = FontStyle.Italic;
                placeholderTextStyle.normal.textColor = Color.grey;
                paramNameRect.xMin += 2;
                EditorGUI.LabelField(paramNameRect, "Parameter name", placeholderTextStyle);
            }

            // Motion property field
            EditorGUI.PropertyField(motionRect, motionProp, GUIContent.none);

            // High priority toggle
            var animClipIconCyan = new Color(127f/255f, 252f/255f, 228f/255f, 1);
            CustomSimpleToggle(hpToggleRect, false, lowPriorityProp, highPriorityIcon, 1, animClipIconCyan);

            // AutoBlend toggle
            if (motionProp.objectReferenceValue == null || motionProp.objectReferenceValue is AnimationClip) {
                CustomSimpleToggle(abToggleRect, false, disableAutoBlendProp, autoBlendIcon, 0, Color.white);
            }
        };
    }

    void InitManualOverridesArrayFoldout()
    {
        manualOverrideGesturesList = new CustomLayoutArrayFoldout(serializedObject, manualOverrideExpressionsProp);
        manualOverrideGesturesList.list.headerHeight = 0;
        manualOverrideGesturesList.list.elementHeight = EditorGUIUtility.singleLineHeight;
        manualOverrideGesturesList.list.drawElementCallback =  
        (Rect rect, int index, bool isActive, bool isFocused) => {
            
            if (manualOverrideExpressionsProp == null ||
                manualOverrideExpressionsProp.arraySize <= index)
            {
                return;
            }

            float h = EditorGUIUtility.singleLineHeight;
            float hpad = Mathf.Max(0, rect.height - h) / 2;
            float leftpad = 8;
            Rect newRect = new Rect(rect.x+leftpad, rect.y+hpad, Mathf.Max(0,rect.width-leftpad), h);

            Rect prefixRect = new Rect(newRect.x, newRect.y, newRect.width * 0.3f, newRect.height);
            Rect propRect = new Rect(prefixRect.xMax, newRect.y, newRect.xMax-prefixRect.xMax, newRect.height);

            SerializedProperty element = manualOverrideExpressionsProp.GetArrayElementAtIndex(index);
            EditorGUI.HandlePrefixLabel(newRect, prefixRect, new GUIContent($"Value {index+1}"));
            EditorGUI.PropertyField(propRect, element, GUIContent.none);
        };
    }

    void InitEyesClosedArrayFoldout()
    {
        eyesClosedMotionsList = new CustomLayoutArrayFoldout(serializedObject, eyesClosedMotionsProp);
        eyesClosedMotionsList.list.headerHeight = 0;
        eyesClosedMotionsList.list.elementHeight = EditorGUIUtility.singleLineHeight;
        eyesClosedMotionsList.list.drawElementCallback =  
        (Rect rect, int index, bool isActive, bool isFocused) => {

            if (eyesClosedMotionsProp == null ||
                eyesClosedMotionsProp.arraySize <= index)
            {
                return;
            }

            float h = EditorGUIUtility.singleLineHeight;
            float hpad = Mathf.Max(0, rect.height - h) / 2;
            float leftpad = 8;
            Rect newRect = new Rect(rect.x+leftpad, rect.y+hpad, Mathf.Max(0,rect.width-leftpad), h);

            Rect prefixRect = new Rect(newRect.x, newRect.y, newRect.width * 0.3f, newRect.height);
            Rect propRect = new Rect(prefixRect.xMax, newRect.y, newRect.xMax-prefixRect.xMax, newRect.height);

            SerializedProperty element = eyesClosedMotionsProp.GetArrayElementAtIndex(index);
            EditorGUI.HandlePrefixLabel(newRect, prefixRect, new GUIContent(element.displayName));
            EditorGUI.PropertyField(propRect, element, GUIContent.none);
        };
    }

    // For the toggles on the contact expressions list
    void CustomSimpleToggle(Rect rect, bool onVal, SerializedProperty prop, GUIContent content, float iconPadTop, Color tint)
    {
        var style = new GUIStyle(GUIStyle.none);
        var ccolSave = GUI.contentColor;
        var colSave = GUI.color;

        // Draw hover rect
        if (rect.Contains(Event.current.mousePosition)) {
            EditorGUI.DrawRect(rect, new Color(1,1,1,0.5f));
        }

        // Update tooltip to indicate enabled state since I'm not sure if the icon fadeout
        // alone will be sufficiently clear for everyone (vision impairment, etc)
        string tooltipSave = content.tooltip;
        if (prop.boolValue == onVal) {
            content.tooltip = "[Currently ON] " + tooltipSave;
        }
        else {
            content.tooltip = "[Currently OFF] " + tooltipSave;
            // Fadeout icon when disabled
            tint.a = 0.2f; 
        }
        GUI.color = tint;

        float iconPad = 0.5f;
        Rect iconRect = new Rect(rect.x+iconPad, rect.y+iconPad+iconPadTop, rect.width-iconPad*2, rect.height-(iconPad+iconPadTop)*2);

        GUI.Label(iconRect, content, GUIStyle.none);
        content.tooltip = tooltipSave;

        // Set the GUI/content color to clear so that the PropertyField doesn't
        // actually draw anything. We just want it for the function effect
        // of toggling the bool property, and also, handling prefab override
        // stuff (e.g. having a right-click menu for "Revert")
        GUI.color = Color.clear;
        GUI.contentColor = Color.clear;

        EditorGUI.PropertyField(rect, prop, GUIContent.none);

        GUI.contentColor = ccolSave;
        GUI.color = colSave;
    }

    class CustomLayoutArrayFoldout
    {
        const float kHeaderPadding = 2f; // Apparently 3 in the original code, but 2 looks more correct
        const float kArraySizeWidth = 48f;
        const float kDefaultFoldoutHeaderHeight = 18f;
        const float kIndentPerLevel = 15f;

        public GUIContent headerContent = null;
        public SerializedProperty arrayProp = null;
        public SerializedProperty arraySizeProp = null;
        public ReorderableList list = null; // ReorderableList is from 'UnityEditorInternal' namespace, but it's public for some reason.

        public CustomLayoutArrayFoldout(SerializedObject obj, SerializedProperty prop)
        {
            this.arrayProp = prop;
            headerContent = new GUIContent(prop.displayName);
            arraySizeProp = prop.FindPropertyRelative("Array.size");
            list = new ReorderableList(obj, prop,
                /* draggable = */           true,
                /* displayHeader = */       false,
                /* displayAddButton = */    true,
                /* displayRemoveButton = */ true);
        }

        public void Draw()
        {
            arrayProp.isExpanded = DrawArrayFoldoutHeader(arrayProp.isExpanded, headerContent, arrayProp, arraySizeProp);
            if (arrayProp.isExpanded) {
                EditorGUILayout.Space(kHeaderPadding);
                DoLayoutListIndented(list);
            }
        }

        // Based on ReorderableListWrapper which is what Unity uses to draw arrays.
        // Unfortunately the class is "internal".
        // https://github.com/Unity-Technologies/UnityCsReference/blob/1b4b79be1f4bedfe18965946323fd565702597ac/Editor/Mono/Inspector/ReorderableListWrapper.cs#L150C9-L212
        static public bool DrawArrayFoldoutHeader(bool foldout, GUIContent content, SerializedProperty arrayProp, SerializedProperty arraySizeProp)
        {
            Rect backgroundRect = GUILayoutUtility.GetRect(content, EditorStyles.foldoutHeader);
            backgroundRect = EditorGUI.IndentedRect(backgroundRect);

            if (arrayProp != null) {
                EditorGUI.BeginProperty(backgroundRect, GUIContent.none, arrayProp);
            }

            bool showArraySize = (arraySizeProp != null);

            var previousEvent = Event.current.type;
            // Unity's actual equation for this "extra indent width" is:
            //    EditorGUI.indent * EditorGUI.indentLevel
            // but EditorGUI.indent already accounts for the indentLevel,
            // so the expanded equation is:
            //    (EditorGUI.indentLevel * kIndentPerLevel) * EditorGUI.indentLevel
            // This seems like a mistake. Maybe they never tested it with
            // indentLevel being > 1.
            // See here:
            // https://github.com/Unity-Technologies/UnityCsReference/blob/1b4b79be1f4bedfe18965946323fd565702597ac/Editor/Mono/Inspector/ReorderableListWrapper.cs#L155
            // https://github.com/Unity-Technologies/UnityCsReference/blob/1b4b79be1f4bedfe18965946323fd565702597ac/Editor/Mono/EditorGUI.cs#L3703
            float extraIndentWidth = EditorGUI.indentLevel * kIndentPerLevel;

            Rect sizeRect = new Rect(backgroundRect.xMax - kArraySizeWidth - extraIndentWidth, backgroundRect.y,
                    kArraySizeWidth + extraIndentWidth, kDefaultFoldoutHeaderHeight);
            if (showArraySize) {
                if (Event.current.type == EventType.MouseUp && sizeRect.Contains(Event.current.mousePosition)) {
                    Event.current.type = EventType.Used;
                }
            }

            foldout = EditorGUI.BeginFoldoutHeaderGroup(backgroundRect, foldout, content);
            EditorGUI.EndFoldoutHeaderGroup();

            if (showArraySize) {
                if (Event.current.type == EventType.Used && sizeRect.Contains(Event.current.mousePosition)) {
                    Event.current.type = previousEvent;
                }

                // EditorGUI.BeginChangeCheck(); // Not sure if this is needed?
                EditorGUI.PropertyField(sizeRect, arraySizeProp, GUIContent.none);
                EditorGUI.LabelField(sizeRect, new GUIContent("", "Array Size"));
                // if (EditorGUI.EndChangeCheck()) ;
            }

            if (arrayProp != null) {
                EditorGUI.EndProperty();
            }

            return foldout;
        }

        // Based on https://github.com/Unity-Technologies/UnityCsReference/blob/1b4b79be1f4bedfe18965946323fd565702597ac/Editor/Mono/GUI/ReorderableList.cs#L743-L760
        static public void DoLayoutListIndented(ReorderableList list)
        {
            GUILayout.BeginVertical();
            Rect r = GUILayoutUtility.GetRect(10, list.GetHeight(), GUILayout.ExpandWidth(true));
            r = EditorGUI.IndentedRect(r);
            list.DoList(r);
            GUILayout.EndVertical();
        }
    }
}

}