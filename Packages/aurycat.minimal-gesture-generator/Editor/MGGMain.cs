// Comment this to not use AnimatorAsCode.V1.VRC
// The only effect is that disabling blinking for eyes-closed gestures
// won't work if this is commented out.
#define USE_AAC_VRC_EXTENSIONS

using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System;
using System.Collections.Generic;
using System.Linq;
using AnimatorAsCode.V1;
#if USE_AAC_VRC_EXTENSIONS
using AnimatorAsCode.V1.VRC;
#endif
using Random = UnityEngine.Random;

namespace aurycat.MGG
{

public class MGGMain
{
    public const string GeneratedLayerName = "Expressions";
    public const float FloatParamThreshold = 0.001f;

    static public void Generate(MinimalGestureGenerator target, bool multi)
    {
        string multiText = multi ? $" on '{target.name}'" : "";

        if (target.AvatarRoot == null) {
            Debug.LogError($"No Avatar Root object set{multiText}.");
            return;
        }

        if (target.FXController == null) {
            Debug.LogError($"No FX Controller set{multiText}.");
            return;
        }

        if (target.AssetContainer == null) {
            // Shouldn't happen because editor will set it if null
            Debug.LogError($"No Asset Container set{multiText}.");
            return;
        }

        // Check controller doesn't have conflicting parameters
        {
            (string, string, AnimatorControllerParameterType, string)[] neededParams = {
                (target.PauseHandGesturesParamName,       "a Bool", AnimatorControllerParameterType.Bool, "'Pause Hand Gestures'"),
                (target.PauseContactExpressionsParamName, "a Bool", AnimatorControllerParameterType.Bool, "'Pause Contact Expressions'"),
                (target.ManualOverrideParamName,          "an Int", AnimatorControllerParameterType.Int,  "'Manual Override'"),
            };

            foreach (var neededParam in neededParams) {
                var p = target.FXController.parameters.FirstOrDefault(p => p.name == neededParam.Item1);
                if (p != null && p.type != neededParam.Item3) {
                    Debug.LogError(
$"The FX Controller{multiText} has an existing parameter '{neededParam.Item1}' " +
$"but it is not {neededParam.Item2} type (it is a {p.type.ToString()}). Please " +
$"remove that parameter from the controller, or choose a different name for the " +
$"{neededParam.Item4} parameter, and then try again.");
                    return;
                }
            }
        }


        //
        // Cleanup input motion lists
        //

        int[] enabledHandGestureIndices;
        Motion[] handGestureMotions;
        PrepareHandGestureMotions(target, out enabledHandGestureIndices, out handGestureMotions);

        Motion[] contactMotionsHP;
        Motion[] contactMotionsOriginalHP;
        string[] contactParamNamesHP;
        Motion[] contactMotionsLP;
        Motion[] contactMotionsOriginalLP;
        string[] contactParamNamesLP;
        if (!PrepareContactExpressionMotions(target,
                out contactMotionsHP, out contactMotionsOriginalHP, out contactParamNamesHP,
                out contactMotionsLP, out contactMotionsOriginalLP, out contactParamNamesLP,
                multiText)) {
            return;
        }

        Motion[] manualOverrideMotions;
        int[] manualOverrideMotionIndices;
        PrepareManualOverrideMotions(target, out manualOverrideMotions, out manualOverrideMotionIndices, multiText);

        Motion[] eyesClosedMotions;
        PrepareEyesClosedMotions(target, handGestureMotions, out eyesClosedMotions, multiText);
        bool anyEyesClosedMotions =
#if USE_AAC_VRC_EXTENSIONS
            eyesClosedMotions.Length > 0;
#else
            false;
#endif
        EyesClosedInfo handGestureMotionsEyesClosed    = GetEyesClosedInfo(eyesClosedMotions, handGestureMotions.Select(m => (m ?? handGestureMotions[0])));
        EyesClosedInfo contactMotionsHPEyesClosed      = GetEyesClosedInfo(eyesClosedMotions, contactMotionsOriginalHP.Select(m => (m ?? handGestureMotions[0])));
        EyesClosedInfo contactMotionsLPEyesClosed      = GetEyesClosedInfo(eyesClosedMotions, contactMotionsOriginalLP.Select(m => (m ?? handGestureMotions[0])));
        EyesClosedInfo manualOverrideMotionsEyesClosed = GetEyesClosedInfo(eyesClosedMotions, manualOverrideMotions.Select(m => (m ?? handGestureMotions[0])));

        //
        // Prepare all-bindings motion lists
        //

        Motion neutralMotion = handGestureMotions[0];

        int totalNumMotions =
              handGestureMotions.Length
            + contactMotionsHP.Length
            + contactMotionsLP.Length
            + manualOverrideMotions.Length
            + (target.UseIsLocalCheck ? 1 : 0);

        Motion[] allMotions = new Motion[totalNumMotions];
        int end = 0;
        handGestureMotions.CopyTo(allMotions, end);    end += handGestureMotions.Length;
        contactMotionsHP.CopyTo(allMotions, end);      end += contactMotionsHP.Length;
        contactMotionsLP.CopyTo(allMotions, end);      end += contactMotionsLP.Length;
        manualOverrideMotions.CopyTo(allMotions, end); end += manualOverrideMotions.Length;

        if (target.UseIsLocalCheck) {
            allMotions[end] = target.IsRemoteClip;
            end += 1;
            if (target.IsLocalClip != null) {
                bool hadNeutralMotion = (neutralMotion != null);
                neutralMotion = MergeClips(neutralMotion, target.IsLocalClip);
                if (!hadNeutralMotion) {
                    neutralMotion.name = "AutoNeutral";
                }
            }
        }

        Motion[] allMotionsAllBindings;
        Motion[] allGeneratedMotions; // Includes neutral
        Motion neutralMotionAllBindings;
        EditorCurveBinding[] missingBindings;
        AnimationClip allBindingsClip;
        bool OK = RecreateMotionsToHaveAllTheSameAnimatedProperties(
            allMotions,
            neutralMotion,
            target.AvatarRoot.gameObject,
            out allMotionsAllBindings,
            out allGeneratedMotions,
            out neutralMotionAllBindings,
            out missingBindings,
            out allBindingsClip);

        if (!OK) {
            Debug.LogError(
$"One or more of the animations{multiText} animates properties that are not " +
"present in the Neutral animation or the avatar hierarchy. The missing " +
"properties are listed below. Please ensure that the Neutral animation " +
"has an animation curve for all these properties, or that the animated " +
"GameObject is in the avatar hierarchy.");
            Debug.LogError("Missing animation curves:  " + string.Join(",  ", missingBindings.Select(b => $"{b.path}::{b.propertyName}")));
            return;
        }

        bool anyClips = allGeneratedMotions.Any(m => m != null && m is AnimationClip && m != neutralMotionAllBindings);

        if (!anyClips) {
            Debug.LogWarning($"No animation clips set{multiText}.");
        }

        if (allMotionsAllBindings.Length != totalNumMotions) {
            throw new Exception($"Wrong length for allMotionsAllBindings array, got {allMotionsAllBindings.Length}, expected {totalNumMotions}. This is a bug.");
        }

        Motion[] handGestureMotionsAllBindings = new Motion[handGestureMotions.Length];
        Motion[] contactMotionsHPAllBindings = new Motion[contactMotionsHP.Length];
        Motion[] contactMotionsLPAllBindings = new Motion[contactMotionsLP.Length];
        Motion[] manualOverrideMotionsAllBindings = new Motion[manualOverrideMotions.Length];

        end = 0;
        Array.Copy(allMotionsAllBindings, end,
                   handGestureMotionsAllBindings, 0,
                   handGestureMotionsAllBindings.Length);
        end += handGestureMotionsAllBindings.Length;
        Array.Copy(allMotionsAllBindings, end,
                   contactMotionsHPAllBindings, 0,
                   contactMotionsHPAllBindings.Length);
        end += contactMotionsHPAllBindings.Length;
        Array.Copy(allMotionsAllBindings, end,
                   contactMotionsLPAllBindings, 0,
                   contactMotionsLPAllBindings.Length);
        end += contactMotionsLPAllBindings.Length;
        Array.Copy(allMotionsAllBindings, end,
                   manualOverrideMotionsAllBindings, 0,
                   manualOverrideMotionsAllBindings.Length);
        end += manualOverrideMotionsAllBindings.Length;

        AnimationClip isRemoteClipAllBindings = null;
        if (target.UseIsLocalCheck) {
            isRemoteClipAllBindings = allMotionsAllBindings[end] as AnimationClip;
            end += 1;
        }


        //
        // Setup animator controller output using AnimatorAsCode
        //

        AacConfiguration config = GetAACConfig(target);
        AacFlBase aac = AacV1.Create(config);

        // Save newly generated clips to the asset container
        aac.ClearPreviousAssets(); // Only clears assets with the given AssetKey
        int numsaved = 0;
        for (int i = 0; i < allGeneratedMotions.Length; i++) {
            SaveAssetToContainer(config, allGeneratedMotions[i], ref numsaved);
        }


        //
        // Generate expressions layer
        //

        // Find existing layer MGG anim layer, if present
        AnimatorControllerLayer existingLayer =
            target.FXController.layers.FirstOrDefault(l => l.name == config.SystemName);

        // CreateMainArbitraryControllerLayer already clears the layer,
        // but MGGAnimatorUtils.ClearLayer can do it MUCH faster, and without
        // leaking subassets. See ClearLayer's header comment for more info.
        if (existingLayer != null) {
            MGGAnimatorUtils.ClearLayer(target.FXController, existingLayer);
        }

        AacFlLayer fx = aac.CreateMainArbitraryControllerLayer(target.FXController);

        if (target.Mask != null) {
            fx.WithAvatarMask(target.Mask);
        }
        else {
            AvatarMask mask = GenerateAvatarMaskForClip(allBindingsClip);
            mask.name = "LayerMask";
            SaveAssetToContainer(config, mask, ref numsaved);
            fx.WithAvatarMask(mask);
        }


        bool usePauseHandGestures = target.UsePauseHandGestures;
        bool usePauseContactExpressions = target.UsePauseContactExpressions;
        bool useHandGestures = anyClips && enabledHandGestureIndices.Length > 1 &&
                               // If all hand gesture motions are null/neutral, no need to generate
                               // the state machine. Same as if user set no enabled gestures at all.
                               handGestureMotionsAllBindings.Any(m => m != null && m != handGestureMotionsAllBindings[0]);
        // An empty motion for manual overrides acts like neutral.
        // So if there is at least one motion slot, even if it's empty,
        // still generate the override states.
        bool useManualOverrides = anyClips &&
                                  target.UseManualOverrideExpressions &&
                                  target.ManualOverrideExpressions.Length > 0;
        bool useContactExpressionsHP = anyClips && contactMotionsHPAllBindings.Length > 0;
        bool useContactExpressionsLP = anyClips && contactMotionsLPAllBindings.Length > 0;

        var pauseHandGesturesParam = usePauseHandGestures ? fx.BoolParameter(target.PauseHandGesturesParamName) : null;
        var pauseContactExpressionsParam = usePauseContactExpressions ? fx.BoolParameter(target.PauseContactExpressionsParamName) : null;

        var manualOverrideOnCond = useManualOverrides ? fx.IntParameter(target.ManualOverrideParamName).IsNotEqualTo(0) : null;
        var manualOverrideOffCond = useManualOverrides ? fx.IntParameter(target.ManualOverrideParamName).IsEqualTo(0) : null;

        var contactExpressionHPOnConds = new List<IAacFlCondition>(); // Or'ed together
        var contactExpressionHPOffConds = new List<IAacFlCondition>(); // And'ed together
        if (useContactExpressionsHP) {
            foreach (string p in contactParamNamesHP) {
                contactExpressionHPOnConds.Add(fx.FloatParameter(p).IsGreaterThan(FloatParamThreshold));
                contactExpressionHPOffConds.Add(fx.FloatParameter(p).IsLessThan(FloatParamThreshold));
            }
        }

        AacFlState sLocalCheck = null;
        if (target.UseIsLocalCheck) {
            sLocalCheck = fx.NewState("Local Check");
            sLocalCheck.WithAnimation(isRemoteClipAllBindings);
            sLocalCheck.TransitionsFromAny().When(fx.BoolParameter("IsLocal").IsFalse());
        }

        // Generate the state machine for hand gestures
        AacFlStateMachine smGesture = null;
        AacFlState sNeutralOnly = null;
        if (useHandGestures) {
            var finalGestures = enabledHandGestureIndices
                .Select(i => (i, MGGUtil.StandardGestureNames[i]))
                .ToArray();

            // Convert gesture motions to 2D array for GenerateGestureSM
            var finalGestureMotions = new Motion[finalGestures.Length, finalGestures.Length];
            var finalGestureMotionsEyesClosed = new bool[finalGestures.Length, finalGestures.Length];
            for (int i = 0; i < finalGestures.Length; i++) {
                for (int j = 0; j < finalGestures.Length; j++) {
                    finalGestureMotions[i,j] = handGestureMotionsAllBindings[i*finalGestures.Length + j];
                    finalGestureMotionsEyesClosed[i,j] = handGestureMotionsEyesClosed[i*finalGestures.Length + j];
                }
            }

            smGesture = GenerateGestureSM(
                fx,
                enabledHandGestureIndices,
                finalGestureMotions,
                "GestureLeft",
                "GestureRight",
                manualOverrideOnCond,
                contactExpressionHPOnConds,
                contactMotionsLP.Select(m => m.name).ToArray(),
                contactMotionsLPAllBindings,
                contactParamNamesLP,
                pauseHandGesturesParam,
                pauseContactExpressionsParam,
                anyEyesClosedMotions,
                finalGestureMotionsEyesClosed,
                contactMotionsLPEyesClosed,
                target.TransitionDuration,
                target.TransitionInterruption
            );
        }
        else {
            // No hand gestures are enabled. Just have a single neutral state in place of the gesture state machine.
            sNeutralOnly = fx.NewState("Neutral");
            if (anyClips || neutralMotion != null) {
                sNeutralOnly.WithAnimation(neutralMotionAllBindings);
            }
            else {
                // Leave the state filled with Aac's default empty animation.
            }
            SetStateEyesClosed(sNeutralOnly, anyEyesClosedMotions, handGestureMotionsEyesClosed[0]);
        }

        AacAnimatorNode smGestureOrNeutral = smGesture != null ? smGesture : sNeutralOnly;

        if (target.UseIsLocalCheck) {
            sLocalCheck.TransitionsTo(smGestureOrNeutral).When(fx.BoolParameter("IsLocal").IsTrue());
        }

        // Generate the state machine for handling contact expressions
        AacFlStateMachine smContactExpressionsHP = null;
        if (useContactExpressionsHP) {
            smContactExpressionsHP = fx.NewSubStateMachine("Contact Expressions (High Priority)");

            smContactExpressionsHP.Shift(smGestureOrNeutral, 1, 1);

            bool eyesClosedCanBeGrouped =
                // Don't group if only one element, its nicer to just put the behavior on the state itself
                contactMotionsHPAllBindings.Length > 1 &&
                contactMotionsHPEyesClosed.canBeGrouped;

            AacFlState s = null;
            AacFlTransitionContinuation currentCombinedExitTransition = null;
            for (int i = 0; i < contactMotionsHPAllBindings.Length; i++) {
                bool combinedWithPrevious = false;
                if (i != 0 && contactMotionsHPAllBindings[i] == contactMotionsHPAllBindings[i-1]) {
                    combinedWithPrevious = true;
                }
                else {
                    s = smContactExpressionsHP.NewState(contactMotionsHP[i].name);
                    s.WithAnimation(contactMotionsHPAllBindings[i]);
                }
                if (!eyesClosedCanBeGrouped && !combinedWithPrevious) {
                    SetStateEyesClosed(s, anyEyesClosedMotions, contactMotionsHPEyesClosed[i]);
                }
                smContactExpressionsHP.EntryTransitionsTo(s)
                    .When(contactExpressionHPOnConds[i]);
                if (!combinedWithPrevious) {
                    currentCombinedExitTransition = s.Exits()
                        .WithTransitionDurationSeconds(target.TransitionDuration)
                        .WithConditionalDestinationInterruption(target.TransitionInterruption)
                        .WhenConditions();
                }
                currentCombinedExitTransition.And(contactExpressionHPOffConds[i]);
                if (useManualOverrides && !combinedWithPrevious) {
                    s.Exits()
                        .WithTransitionDurationSeconds(target.TransitionDuration)
                        .WithConditionalDestinationInterruption(target.TransitionInterruption)
                        .When(manualOverrideOnCond);
                }
                if (usePauseContactExpressions && !combinedWithPrevious) {
                    s.Exits()
                        .WithTransitionDurationSeconds(target.TransitionDuration)
                        .WithConditionalDestinationInterruption(target.TransitionInterruption)
                        .When(pauseContactExpressionsParam.IsTrue());
                }
            }

            if (eyesClosedCanBeGrouped) {
                SetStateEyesClosed(smContactExpressionsHP, anyEyesClosedMotions, contactMotionsHPEyesClosed[0]);
            }
        }

        // Generate the state machine for handling manual overrides
        AacFlStateMachine smManualOverrides = null;
        if (useManualOverrides) {
            smManualOverrides = fx.NewSubStateMachine("Manual Override Expressions");

            smManualOverrides.Shift(smGestureOrNeutral, 0, 2);

            // This is in case the override param is set to an invalid value,
            // or a value that had no Motion set.
            var sDefault = smManualOverrides.NewState("[Default]");
            sDefault.WithAnimation(neutralMotionAllBindings);
            if (manualOverrideMotionIndices.Length > 0) {
                // Exit from the default state when the param is 0 or any valid
                // manual override value that has a Motion set. If a value had
                // no Motion, it's removed from the manualOverrideMotion array
                // and so manualOverrideMotionIndices will have a gap.
                int largestIndex = manualOverrideMotionIndices[manualOverrideMotionIndices.Length-1];
                var tDefaultExit = sDefault.Exits()
                    .WithTransitionDurationSeconds(target.TransitionDuration)
                    .WithConditionalDestinationInterruption(target.TransitionInterruption)
                    .WhenConditions();
                tDefaultExit.And(fx.IntParameter(target.ManualOverrideParamName).IsGreaterThan(-1));
                tDefaultExit.And(fx.IntParameter(target.ManualOverrideParamName).IsLessThan(largestIndex+1));
                int prev = 0;
                for (int i = 0; i < manualOverrideMotionIndices.Length; i++) {
                    if (manualOverrideMotionIndices[i] <= prev) {
                        throw new Exception($"manualOverrideMotionIndices[{i}] is {manualOverrideMotionIndices[i]} which is <= {prev}. This is a bug.");
                    }
                    if (manualOverrideMotionIndices[i] != prev + 1) {
                        for (int j = prev + 1; j < manualOverrideMotionIndices[i]; j++) {
                            tDefaultExit.And(fx.IntParameter(target.ManualOverrideParamName).IsNotEqualTo(j));
                        }
                    }
                    prev = manualOverrideMotionIndices[i];
                }

                // Can be grouped if all have the same eyes-closed value (all true or all false)
                bool eyesClosedCanBeGrouped =
                    manualOverrideMotionsAllBindings.Length > 0 &&
                    manualOverrideMotionsEyesClosed.canBeGrouped &&
                    // Make sure that neutral, used above for [Default], is also the same as manual overrides
                    handGestureMotionsEyesClosed[0] == manualOverrideMotionsEyesClosed[0];

                for (int i = 0; i < manualOverrideMotionsAllBindings.Length; i++) {
                    var s = smManualOverrides.NewState(manualOverrideMotions[i].name);
                    s.WithAnimation(manualOverrideMotionsAllBindings[i]);
                    if (!eyesClosedCanBeGrouped) {
                        SetStateEyesClosed(s, anyEyesClosedMotions, manualOverrideMotionsEyesClosed[i]);
                    }
                    smManualOverrides.EntryTransitionsTo(s)
                        .When(fx.IntParameter(target.ManualOverrideParamName).IsEqualTo(manualOverrideMotionIndices[i]));
                    s.Exits()
                        .WithTransitionDurationSeconds(target.TransitionDuration)
                        .WithConditionalDestinationInterruption(target.TransitionInterruption)
                        .When(fx.IntParameter(target.ManualOverrideParamName).IsNotEqualTo(manualOverrideMotionIndices[i]));
                }

                if (eyesClosedCanBeGrouped) {
                    SetStateEyesClosed(smManualOverrides, anyEyesClosedMotions, manualOverrideMotionsEyesClosed[0]);
                }
                else {
                    SetStateEyesClosed(sDefault, anyEyesClosedMotions, handGestureMotionsEyesClosed[0]);
                }
            }
            else {
                sDefault.Exits()
                    .WithTransitionDurationSeconds(target.TransitionDuration)
                    .WithConditionalDestinationInterruption(target.TransitionInterruption)
                    .When(fx.IntParameter(target.ManualOverrideParamName).IsEqualTo(0));
            }
        }

        // Transitions out of smGesture/sNeutralOnly
        {
            // Transition to manual overrides if one of those is enabled
            if (useManualOverrides) {
                smGestureOrNeutral.TransitionsTo(smManualOverrides)
                    .When(manualOverrideOnCond);
            }

            // Otherwise, transition to contact expressions if one of those is enabled
            if (useContactExpressionsHP) {
                foreach (var cond in contactExpressionHPOnConds) {
                    var t = smGestureOrNeutral.TransitionsTo(smContactExpressionsHP).When(cond);
                    if (usePauseContactExpressions) {
                        t.And(pauseContactExpressionsParam.IsFalse());
                    }
                }
            }

            // Otherwise, return to self
            if (smGesture != null) {
                smGesture.Restarts();
            }
        }

        // Transitions out of smContactExpressions
        if (useContactExpressionsHP) {
            // Transition to manual overrides if one is enabled
            if (useManualOverrides) {
                smContactExpressionsHP.TransitionsTo(smManualOverrides)
                    .When(manualOverrideOnCond);
            }

            // Otherwise, return to smGesture/sNeutral if contant expressions are paused
            if (usePauseContactExpressions) {
                smContactExpressionsHP.TransitionsTo(smGestureOrNeutral)
                    .When(pauseContactExpressionsParam.IsTrue());
            }

            // Otherwise, return to smGesture/sNeutral if no contact expressions are enabled
            var tFromContactExpressions = smContactExpressionsHP.TransitionsTo(smGestureOrNeutral).WhenConditions();
            foreach (var cond in contactExpressionHPOffConds) {
                tFromContactExpressions.And(cond);
            }

            // Otherwise, some contact expression is still enabled, so return to self
            smContactExpressionsHP.Restarts();
        }

        // Transitions out of smManualOverrides
        if (useManualOverrides) {
            // Transition to self if a manual override is still enabled
            smManualOverrides.TransitionsTo(smManualOverrides)
                .When(manualOverrideOnCond);

            // Otherwise, transition to contact expressions if one of those is enabled
            if (useContactExpressionsHP) {
                foreach (var cond in contactExpressionHPOnConds) {
                    var t = smManualOverrides.TransitionsTo(smContactExpressionsHP).When(cond);
                    if (usePauseContactExpressions) {
                        t.And(pauseContactExpressionsParam.IsFalse());
                    }
                }
            }

            // Otherwise, return to smGesture/sNeutral
            smManualOverrides.TransitionsTo(smGestureOrNeutral);
        }
    }

    // Performs some pre-processing on the source hand gesture motions, as
    // stored in the HandGestureExpressions array, to take into account the
    // combo gesture mode and the set of enabled gestures.
    //
    // 'enabledGestureIndices' output is an array of length <number of enabled
    // gestures> and stores the index for each. For example, if the Neutral,
    // Point, and FingerGun gestures were enabled, the output would be:
    //   [0, 3, 7]
    // Neutral is always enabled, and so 0 will always be the first element.
    //
    // 'enabledGestureMotions' output is an array representing a 2D square array
    // in row major order, where the side lengths are <number of enabled gestures>.
    // Rows represent left-hand gestures, columns represent right-hand gestures.
    // E.g. for gestures A, B, and C, the output as 2D would be
    //   LA+RA LA+RB LA+RC
    //   LB+RA LB+RB LB+RC
    //   LC+RA LC+RB LC+RC
    // and the actual output in 1D would be
    //   LA+RA LA+RB LA+RC LB+RA LB+RB LB+RC LC+RA LC+RB LC+RC
    //
    // Regardless of user input, the output will be in "asymmetric" mode.
    // E.g. if the user is using "symmetric" mode, then all the input motions
    // will be duplicated across the diagonal when creating the square array.
    static public void PrepareHandGestureMotions(
        MinimalGestureGenerator target,
        out int[] enabledGestureIndices,
        out Motion[] enabledGestureMotions)
    {
        // Remove elements from the list that correspond to disabled gestures.
        Func<int, bool> gestureIsEnabled = (i) => i==0 || (((int)target.EnabledGestures) & (1 << i)) == (1 << i);

        enabledGestureIndices = Enumerable
            .Range(0, MGGUtil.NumStandardGestures)
            .Where(i => gestureIsEnabled(i))
            .ToArray();

        // Combo mode remap. Converts all modes into the equivalent values for "asymmetric" mode.
        Motion[] handGestureMotions = MGGUtil.RemapComboGestureArrayUsingMode(target.HandGestureExpressions, target.ComboGestureMode);

        int sideLength = enabledGestureIndices.Length;
        enabledGestureMotions = new Motion[sideLength * sideLength];

        int k = 0;
        for (int i = 0; i < MGGUtil.NumStandardGestures; i++) {
            if (!gestureIsEnabled(i)) { continue; }
            for (int j = 0; j < MGGUtil.NumStandardGestures; j++) {
                if (!gestureIsEnabled(j)) { continue; }
                enabledGestureMotions[k] = handGestureMotions[i*MGGUtil.NumStandardGestures + j];
                k++;
            }
        }

        // Replace Unity's "fake null" with a real null, so that ?? operator works
        // https://blog.unity.com/engine-platform/custom-operator-should-we-keep-it
        for (int i = 0; i < enabledGestureMotions.Length; i++) {
            if (enabledGestureMotions[i] == null) {
                enabledGestureMotions[i] = null;
            }
        }
    }

    static public bool PrepareContactExpressionMotions(
        MinimalGestureGenerator target,
        out Motion[] out_contactMotionsHP, // high priority (higher priority than hand gestures, but still lower than manual override)
        out Motion[] out_contactMotionsOriginalHP,
        out string[] out_contactParamNamesHP,
        out Motion[] out_contactMotionsLP, // low priority (lower priority than hand gestures)
        out Motion[] out_contactMotionsOriginalLP,
        out string[] out_contactParamNamesLP,
        string multiText)
    {
        if (!target.UseContactExpressions) {
            out_contactMotionsHP = new Motion[0];
            out_contactMotionsOriginalHP = new Motion[0];
            out_contactParamNamesHP = new string[0];
            out_contactMotionsLP = new Motion[0];
            out_contactMotionsOriginalLP = new Motion[0];
            out_contactParamNamesLP = new string[0];
            return true;
        }

        var entries = new List<(ContactExpressionInfo info, Motion finalMotion)>();

        for (int i = 0; i < target.ContactExpressions.Length; i++) {
            var ce = target.ContactExpressions[i];

            if (String.IsNullOrEmpty(ce.paramName)) {
                Debug.LogWarning($"Contact expression {i+1}{multiText} has an empty parameter name. The expression will be ignored.");
                continue;
            }
            else if (ce.motion == null) {
                Debug.LogWarning($"Contact expression {i+1} ({ce.paramName}){multiText} has no Motion field set. The neutral Motion will be used instead.");
            }

            var p = target.FXController.parameters.FirstOrDefault(p => p.name == ce.paramName);
            if (p != null && p.type != AnimatorControllerParameterType.Float) {
                Debug.LogError(
$"Contact expression {i+1}{multiText} specifies the parameter '{ce.paramName}' which already " +
$"exists in the FX Controller and is not a Float type (it is a {p.type.ToString()}). " +
"Please use a different parameter for the contact expression, or remove the parameter from " +
"the FX Controller, and then try again.");
                out_contactMotionsHP = null;
                out_contactMotionsOriginalHP = null;
                out_contactParamNamesHP = null;
                out_contactMotionsLP = null;
                out_contactMotionsOriginalLP = null;
                out_contactParamNamesLP = null;
                return false;
            }

            entries.Add((ce, null));
        }

        // Apply auto-blend
        for (int i = 0; i < entries.Count; i++) {
            bool autoBlend = !entries[i].info.disableAutoBlend;
            if (autoBlend && entries[i].info.motion != null && entries[i].info.motion is AnimationClip) {
                Motion generatedMotion = GenerateContactExpressionBlendTree(entries[i].info.motion as AnimationClip, entries[i].info.paramName);
                entries[i] = (entries[i].info, generatedMotion);
            }
            else {
                entries[i] = (entries[i].info, entries[i].info.motion);
            }
        }

        // Note the final .Select(m=>(m==null?null:m)) is to replace Unity's
        // "fake" null with a real null, so the ?? operator works
        // https://blog.unity.com/engine-platform/custom-operator-should-we-keep-it
        out_contactMotionsHP          = entries.Where(ce => !ce.info.lowPriority).Select(ce => ce.finalMotion)   .Select(m=>(m==null?null:m)).ToArray();
        out_contactMotionsOriginalHP  = entries.Where(ce => !ce.info.lowPriority).Select(ce => ce.info.motion)   .Select(m=>(m==null?null:m)).ToArray();
        out_contactParamNamesHP       = entries.Where(ce => !ce.info.lowPriority).Select(ce => ce.info.paramName).ToArray();
        out_contactMotionsLP          = entries.Where(ce =>  ce.info.lowPriority).Select(ce => ce.finalMotion)   .Select(m=>(m==null?null:m)).ToArray();
        out_contactMotionsOriginalLP  = entries.Where(ce =>  ce.info.lowPriority).Select(ce => ce.info.motion)   .Select(m=>(m==null?null:m)).ToArray();
        out_contactParamNamesLP       = entries.Where(ce =>  ce.info.lowPriority).Select(ce => ce.info.paramName).ToArray();
        return true;
    }

    static BlendTree GenerateContactExpressionBlendTree(AnimationClip clip, string paramName)
    {
        BlendTree bt = new BlendTree();
        bt.name = clip.name + " AutoBlend";
        bt.blendParameter = paramName;
        bt.blendType = BlendTreeType.Simple1D;
        bt.useAutomaticThresholds = true;
        ChildMotion[] cms = new ChildMotion[2];
        // Leave the 0-value motion null, to be automatically filled in with
        // the neutral clip later on.
        cms[0].motion = null;
        cms[0].threshold = 0;
        cms[0].timeScale = 1;
        cms[1].motion = clip;
        cms[1].threshold = 1;
        cms[1].timeScale = 1;
        bt.children = cms;
        return bt;
    }

    static public void PrepareManualOverrideMotions(
        MinimalGestureGenerator target,
        out Motion[] out_manualOverrideMotions,
        out int[] out_manualOverrideMotionIndices,
        string multiText)
    {
        if (!target.UseManualOverrideExpressions) {
            out_manualOverrideMotions = new Motion[0];
            out_manualOverrideMotionIndices = new int[0];
            return;
        }

        // Remove empty entries from manual override motions list, but save
        // the original (1-based) position in the array since that's what the
        // ManualOverride parameter will use to select a motion.
        //
        // Removed entries will act like neutral in the animator.
        Motion[] manualOverrideMotions = target.ManualOverrideExpressions;
        for (int i = 0; i < manualOverrideMotions.Length; i++) {
            if (manualOverrideMotions[i] == null) {
                Debug.LogWarning($"Manual override expression {i+1}{multiText} has no Motion field set. The neutral Motion will be used instead.");
            }
        }
        int[] manualOverrideMotionIndices =
            Enumerable.Range(1, manualOverrideMotions.Length)
                .Where((_,i) => manualOverrideMotions[i] != null)
                .ToArray();
        manualOverrideMotions = manualOverrideMotions.Where((m,i) => m != null).ToArray();

        out_manualOverrideMotions = manualOverrideMotions;
        out_manualOverrideMotionIndices = manualOverrideMotionIndices;
    }

    static public void PrepareEyesClosedMotions(
        MinimalGestureGenerator target,
        Motion[] enabledGestureMotions,
        out Motion[] out_eyesClosedMotions,
        string multiText)
    {
        var allMotions =
            enabledGestureMotions
            .Concat(target.ContactExpressions.Select(ce => ce.motion))
            .Concat(target.ManualOverrideExpressions);

        var unused =
            target.EyesClosedMotions
            .Where(m => m != null)
            .Except(allMotions);

        var unusedStr = string.Join(", ", unused.Select(m => m.name));
        if (unusedStr != "") {
            Debug.LogWarning($"These 'Eyes Closed' motions{multiText} are not used by any expression and will be ignored: " + unusedStr);
        }

        out_eyesClosedMotions =
            target.EyesClosedMotions
            .Where(m => m != null)
            .Except(unused)
            .ToArray();
    }

    public struct EyesClosedInfo
    {
        public bool[] array;
        public bool any;
        public bool all;

        public bool this[int i] {
            get => array[i];
            set => array[i] = value;
        }
        public int Length {
            get => array.Length;
        }

        // Can be grouped if all have the same eyes-closed value (all true or all false)
        public bool canBeGrouped {
            get => !any || all;
        }
    }

    static public EyesClosedInfo GetEyesClosedInfo(Motion[] eyesClosedMotions, IEnumerable<Motion> motions)
    {
        EyesClosedInfo i = new EyesClosedInfo();
        i.array = motions.Select(m => eyesClosedMotions.Contains(m)).ToArray();
        i.any = i.array.Contains(true);
        i.all = !i.array.Contains(false);
        return i;
    }

    // Modified from RegisterClip in AacInternals.cs
    static void SaveAssetToContainer(AacConfiguration config, UnityEngine.Object asset, ref int i)
    {
        asset.name = $"zAutogenerated__{config.AssetKey}__{asset.name}_{i}{Random.Range(0, Int32.MaxValue)}";
        asset.hideFlags = HideFlags.None;
        AssetDatabase.AddObjectToAsset(asset, config.AssetContainer);
        i++;
    }

    // Debug print any array of motions, optionally special behavior for
    // combo hand gesture arrays.
    static void DebugPrintMotionsArray(string name, Motion[] motions, bool isHandGesturesArray=false)
    {
        Debug.Log(name + ":");
        for (int i = 0; i < motions.Length; i++) {
            string header = String.Format("{0,2}", $"{i}");
            if (isHandGesturesArray) {
                string mname;
                if (motions.Length == MGGUtil.NumAsymmetricComboGestures) {
                    mname = MGGUtil.AsymmetricComboGestureNames[i];
                }
                else if (motions.Length == MGGUtil.NumSymmetricComboGestures) {
                    mname = MGGUtil.SymmetricComboGestureNames[i];
                }
                else if (motions.Length == MGGUtil.NumSymmetricDoublesComboGestures) {
                    mname = MGGUtil.SymmetricDoublesComboGestureNames[i];
                }
                else {
                    throw new Exception($"Unexpected length for hand gestures array");
                }
                header += String.Format(" ({0,23})", mname);
            }
            Debug.Log($"{header}:\t\t{DebugMotionToString(motions[i])}");
        }
    }

    // Debug print the output of PrepareHandGestureMotions
    static void DebugPrintEnabledHandGesturesArray(int[] gestureIndices, Motion[] motions)
    {
        Debug.Log("Enabled gestures:");
        bool[] enabled = new bool[MGGUtil.NumStandardGestures];
        for (int i = 0; i < gestureIndices.Length; i++) {
            if (gestureIndices[i] >= MGGUtil.NumStandardGestures || gestureIndices[i] < 0) {
                throw new Exception($"Invalid gesture index {gestureIndices[i]}");
            }
            enabled[gestureIndices[i]] = true;
        }

        int k = 0, t = 0;
        for (int i = 0; i < MGGUtil.NumStandardGestures; i++) {
            for (int j = 0; j < MGGUtil.NumStandardGestures; j++) {
                if (!enabled[i] || !enabled[j]) { t++; continue; }
                string header = String.Format("{0,2} ({1,23})", $"{k}", MGGUtil.AsymmetricComboGestureNames[t]);
                Debug.Log($"  {header}:\t\t{DebugMotionToString(motions[k])}");
                k++;
                t++;
            }
        }
    }

    static string DebugMotionToString(Motion m)
    {
        return m == null ? "----" : m.name;
    }

    // 'gestureParamValues' is an array of enabled VRC gesture values.
    // It must always include the neutral gesture, 0. For example, if
    // only HandOpen, Point, and FingerGun were enabled, the array
    // would be [0,2,3,6].
    //
    // 'gestureMotions' should have shape [gestureParamValues.Length, gestureParamValues.Length]
    // where each element is gestureMotions[leftParam, rightParam]
    static public AacFlStateMachine GenerateGestureSM(
        AacFlLayer fx,
        int[] gestureParamValues,
        Motion[,] gestureMotions,
        string leftParamName,
        string rightParamName,
        IAacFlCondition manualOverrideCondition,
        IEnumerable<IAacFlCondition> contactExpressionConditions,
        string[] contactMotionsLPNames,
        Motion[] contactMotionsLP,
        string[] contactParamNamesLP,
        AacFlBoolParameter pauseHandGesturesParam,
        AacFlBoolParameter pauseContactExpressionsParam,
        bool anyEyesClosedMotions,
        bool[,] gestureMotionsEyesClosed,
        EyesClosedInfo contactMotionsLPEyesClosed,
        float transitionDuration,
        bool transitionInterruption)
    {
        if (gestureParamValues.Length == 0) {
            throw new ArgumentException($"gestureParamValues array is empty");
        }
        for (int i = 1; i < gestureParamValues.Length; i++) {
            if (gestureParamValues[i] <= gestureParamValues[i-1]) {
                throw new ArgumentException("gestureParamValues array is not sorted or has duplicate entries");
            }
        }
        if (gestureParamValues[0] != 0) {
            throw new ArgumentException("gestureParamValues array is missing neutral (0) entry");
        }
        if (gestureParamValues[0] < 0) {
            throw new ArgumentException("gestureParamValues array has an entry with negative gesture index");
        }
        if (gestureParamValues.Length == 1) {
            throw new ArgumentException("gestureParamValues array only has the neutral gesture");
        }
        if (gestureMotions.GetLength(0) != gestureParamValues.Length || gestureMotions.GetLength(1) != gestureParamValues.Length) {
            throw new ArgumentException($"gestureMotions array must have shape [{gestureParamValues.Length},{gestureParamValues.Length}], but has shape [{gestureMotions.GetLength(0)},{gestureMotions.GetLength(1)}]");
        }
        if (gestureMotionsEyesClosed.GetLength(0) != gestureParamValues.Length || gestureMotionsEyesClosed.GetLength(1) != gestureParamValues.Length) {
            throw new ArgumentException($"gestureMotionsEyesClosed array must have shape [{gestureParamValues.Length},{gestureParamValues.Length}], but has shape [{gestureMotionsEyesClosed.GetLength(0)},{gestureMotionsEyesClosed.GetLength(1)}]");
        }
        if (contactMotionsLPNames.Length != contactMotionsLP.Length) {
            throw new ArgumentException($"contactMotionsLPNames array must have size {contactMotionsLP.Length}, but has size {contactMotionsLPNames.Length}");
        }
        if (contactParamNamesLP.Length != contactMotionsLP.Length) {
            throw new ArgumentException($"contactParamNamesLP array must have size {contactMotionsLP.Length}, but has size {contactParamNamesLP.Length}");
        }
        if (contactMotionsLPEyesClosed.Length != contactMotionsLP.Length) {
            throw new ArgumentException($"contactMotionsLPEyesClosed array must have size {contactMotionsLP.Length}, but has size {contactMotionsLPEyesClosed.Length}");
        }

        // Can be grouped if all have the same eyes-closed value (all true or all false)
        bool contactMotionsLPeyesClosedCanBeGrouped =
            contactMotionsLP.Length > 0 &&
            contactMotionsLPEyesClosed.canBeGrouped &&
            // Make sure that neutral, used above for [Default], is also the same as manual overrides
            gestureMotionsEyesClosed[0,0] == contactMotionsLPEyesClosed[0];

        ////////////////////////////////////
        ///// Private helper functions /////
        ////////////////////////////////////

        AacFlTransition[] exitState(AacFlState[] state)
        {
            return state.Exits()
                .WithTransitionDurationSeconds(transitionDuration)
                .WithConditionalDestinationInterruption(transitionInterruption);
        }

        void generateEntryTransitionsForState(
            AacAnimatorNode state,
            int[] copyIndices,
            string paramName,
            bool isDefaultState)
        {
            if (isDefaultState) {
                // A single empty "default" transition is added at the end as a catch-all.
            }
            else {
                for (int i = 0; i < copyIndices.Length; i++) {
                    int gestureVal = gestureParamValues[copyIndices[i]];

                    int numConsecutive = 0;
                    int lastConsecutive = gestureVal;
                    for (int j = i+1; j < copyIndices.Length; j++) {
                        int consecutive = gestureParamValues[copyIndices[j]];
                        if (consecutive != lastConsecutive + 1)  {
                            break;
                        }
                        numConsecutive++;
                        lastConsecutive = consecutive;
                    }

                    AacFlIntParameter param = fx.IntParameter(paramName);
                    if (numConsecutive > 0) {
                        state.TransitionsFromEntry()
                            .When(param.IsGreaterThan(gestureVal - 1))
                            .And(param.IsLessThan(lastConsecutive + 1));
                        i += numConsecutive;
                    }
                    else {
                        state.TransitionsFromEntry()
                            .When(param.IsEqualTo(gestureVal));
                    }
                }
            }
        }

        // copyIndices is the indices into gestureParamValues for all the
        // gestures that should be in / stay in this state. Notably, this
        // does not include VRC gesture values that aren't in gestureParamValues
        // to begin with, e.g. if gesture 2 (HandOpen) is disabled, then
        // it won't be in gestureParamValues and so it won't be in copyIndices.
        //
        // See note below about `sRHandSubstates` for why `state` is an array.
        // For the purposes of this function, pretend it's a single state.
        // Aac array extensions allow AacFlState[] to use When()/WhenConditions().
        AacFlTransitionContinuation[] generateExitTransitionsForState(
            AacFlState[] state,
            int[] copyIndices,
            string paramName,
            bool isDefaultState)
        {
            AacFlTransitionContinuation[] conditions = null;

            if (isDefaultState) {
                // Inverse of copyIndices -- all the gestures that should leave
                // this state. Unlike copyIndices, this is always a complete list.
                int[] noncopyIndices = Enumerable.Range(0, gestureParamValues.Length).Except(copyIndices).ToArray();

                // If there are no other gestures to go to (for this particular
                // parameter), then don't generate any exit transition.
                if (noncopyIndices.Length == 0) {
                    // IntParameter below has the side effect of creating the parameter
                    // if it doesn't exist, so return here to avoid calling it until
                    // we actually end up needing the parameter.
                    return null;
                }

                AacFlIntParameter param = fx.IntParameter(paramName);

                // Only one other gesture to go to, simple exit transition
                if (noncopyIndices.Length == 1) {
                    conditions = exitState(state)
                        .When(param.IsEqualTo(gestureParamValues[noncopyIndices[0]]));
                }
                // Multiple possible gestures to go to.
                // Instead of making a separate exit transition for each noncopy gesture,
                // make a single transition which exits if the parameter is
                //   * param >= lowest noncopy gesture, and
                //   * param <= highest noncopy gesture, and
                //   * param is not equal to any of the disabled or copy gestures
                // Even though this generates more conditions than number of transitions
                // that range if we generated a separate exit transition for each,
                // transitions are more expensive so just having one is better.
                else {
                    int[] noncopyGestureParamVals = noncopyIndices.Select(i => gestureParamValues[i]).ToArray();

                    // Generate a list of disabled or copy gestures by inverting
                    // noncopyIndices against a contiguous list of gesture param
                    // values, including ones that are disabled. This list makes
                    // up the conditions for the 3rd bullet in the comment above.
                    int firstGestureVal = noncopyGestureParamVals[0];
                    int lastGestureVal = noncopyGestureParamVals[noncopyGestureParamVals.Length-1];
                    int numGestureValsNoGaps = lastGestureVal - firstGestureVal + 1;
                    int[] copyOrDisabledGestureParamValuesInRange =
                        Enumerable.Range(firstGestureVal, numGestureValsNoGaps)
                        .Except(noncopyGestureParamVals).ToArray();

                    conditions = exitState(state).WhenConditions();
                    conditions.And(param.IsGreaterThan(noncopyGestureParamVals[0]-1));
                    for (int i = 0; i < copyOrDisabledGestureParamValuesInRange.Length; i++) {
                        conditions.And(param.IsNotEqualTo(copyOrDisabledGestureParamValuesInRange[i]));
                    }
                    conditions.And(param.IsLessThan(noncopyGestureParamVals[noncopyGestureParamVals.Length-1] + 1));
                }
            }
            else {
                AacFlIntParameter param = fx.IntParameter(paramName);
                conditions = exitState(state).WhenConditions();
                for (int i = 0; i < copyIndices.Length; i++) {
                    conditions.And(param.IsNotEqualTo(gestureParamValues[copyIndices[i]]));
                }
            }

            return conditions;
        }

        string nameForGestureState(
            string prefix,
            string handLetter,
            int[] copyIndices)
        {
            string name = "";
            if (prefix != null) {
                name += prefix + " - ";
            }
            for (int i = 0; i < copyIndices.Length; i++) {
                if ( i > 0 ) {
                    name += ", ";
                }
                int paramValue = gestureParamValues[copyIndices[i]];
                name += handLetter + paramValue;
                if (copyIndices.Length < 3) {
                    name += "(" + MGGUtil.StandardGestureNames[paramValue] + ")";
                }
            }
            return name;
        }

        // Generate a LP Contact Expression state machine in place of a neutral state
        // (any state which has the motion gestureMotions[0,0]). Outputs an array of
        // all the states within, because they need to get exit transitions put on
        // them just like all the other non-neutral states.
        AacAnimatorNode generateLPContactExpressionsSM(
            AacFlStateMachine parent,
            string name,
            out AacFlState[] out_states)
        {
            AacFlStateMachine sm = parent.NewSubStateMachine(name);
            AacFlState neutral = sm.NewState("Neutral");
            neutral.WithAnimation(gestureMotions[0,0]);

            out_states = new AacFlState[1 + contactMotionsLP.Length];
            out_states[0] = neutral;

            AacFlState[] cstates = new AacFlState[contactMotionsLP.Length];
            AacFlTransitionContinuation currentCombinedExitTransition = null;
            bool anyCombined = false;
            for (int i = 0; i < contactMotionsLP.Length; i++) {
                var paramName = contactParamNamesLP[i];

                bool combinedWithPrevious = false;
                if (i != 0 && contactMotionsLP[i] == contactMotionsLP[i-1]) {
                    cstates[i] = cstates[i-1];
                    combinedWithPrevious = true;
                    anyCombined = true;
                }
                else {
                    cstates[i] = sm.NewState(contactMotionsLPNames[i]);
                    cstates[i].WithAnimation(contactMotionsLP[i]);
                }

                if (!contactMotionsLPeyesClosedCanBeGrouped && !combinedWithPrevious) {
                    SetStateEyesClosed(cstates[i], anyEyesClosedMotions, contactMotionsLPEyesClosed[i]);
                }
                var entry = sm.EntryTransitionsTo(cstates[i])
                    .When(fx.FloatParameter(paramName).IsGreaterThan(FloatParamThreshold));
                if (pauseContactExpressionsParam != null) {
                    entry.And(pauseContactExpressionsParam.IsFalse());
                }

                // Transition to other contact motion states, giving higher priority to
                // ones earlier in the list. Overall this will make 1+2+3+4... transitions
                // for the number of these contactMotionsLP states which is not ideal but
                // there shouldn't normally be many of these. Probably just one or two!
                if (!combinedWithPrevious) {
                    for (int j = 0; j < i; j++) {
                        cstates[i].TransitionsTo(cstates[j])
                            .WithTransitionDurationSeconds(transitionDuration)
                            .WithConditionalDestinationInterruption(transitionInterruption)
                            .When(fx.FloatParameter(contactParamNamesLP[j]).IsGreaterThan(FloatParamThreshold));
                    }
                }

                if (!combinedWithPrevious) {
                    currentCombinedExitTransition = cstates[i].TransitionsTo(neutral)
                        .WithTransitionDurationSeconds(transitionDuration)
                        .WithConditionalDestinationInterruption(transitionInterruption)
                        .WhenConditions();
                }
                currentCombinedExitTransition
                    .And(fx.FloatParameter(paramName).IsLessThan(FloatParamThreshold));

                var tNeutralToCstate = neutral.TransitionsTo(cstates[i])
                    .WithTransitionDurationSeconds(transitionDuration)
                    .WithConditionalDestinationInterruption(transitionInterruption)
                    .When(fx.FloatParameter(paramName).IsGreaterThan(FloatParamThreshold));

                if (pauseContactExpressionsParam != null) {
                    if (!combinedWithPrevious) {
                        cstates[i].TransitionsTo(neutral)
                            .WithTransitionDurationSeconds(transitionDuration)
                            .WithConditionalDestinationInterruption(transitionInterruption)
                            .When(pauseContactExpressionsParam.IsTrue());
                    }
                    tNeutralToCstate
                        .And(pauseContactExpressionsParam.IsFalse());
                }

                out_states[i+1] = combinedWithPrevious ? null : cstates[i];
            }

            if (anyCombined) {
                out_states = out_states.Where(x => x != null).ToArray();
            }

            if (!contactMotionsLPeyesClosedCanBeGrouped) {
                SetStateEyesClosed(neutral, anyEyesClosedMotions, gestureMotionsEyesClosed[0,0]);
            }
            else {
                // Don't put the Tracking behavior onto `sm` yet, leave it
                // for the outer state machine to decide if eyes-closed can
                // be grouped again out to the LHand state machine.
            }

            // This substatemachine can unconditionally exit to the parent, because the
            // conditions for exit will be on the inside exit transitions created later
            // (created on each of the out_states).
            sm.Exits();

            return sm;
        }

        ////////////////////////////////////////
        ///// End private helper functions /////
        ////////////////////////////////////////

        int[][] uniqueLHandGestures = FindUnique(gestureParamValues.Length,
            // Two LHand state machines are equal if all their contained
            // right-hand motions are equal
            (a,b) => {
                for (int i = 0; i < gestureParamValues.Length; i++) {
                    if (gestureMotions[a,i] != gestureMotions[b,i]) {
                        return false;
                    }
                }
                return true;
            });

        AacFlStateMachine smRoot = fx.NewSubStateMachine("Gesture Expressions");
        AacAnimatorNode[] all_sLHands = new AacAnimatorNode[uniqueLHandGestures.Length];
        for (int i = 0; i < uniqueLHandGestures.Length; i++) {
            // The "copies" represent all the gestures which use the same motions
            // and so can be combined into the same state. Each entry in this array
            // is an index into gestureParamValues or gestureMotions. By definition,
            // gestureMotion[lCopyIndices[X], Y] for any Y are the same for all X.
            int[] lCopyIndices = uniqueLHandGestures[i];
            // There is always at least one "copy" for a gesture. This is the primary.
            int lGestureIndex = lCopyIndices[0];

            int[][] uniqueRHandGestures = FindUnique(gestureParamValues.Length,
                // Two RHand states are equal if their motions are equal
                (a,b) => gestureMotions[lGestureIndex,a] == gestureMotions[lGestureIndex,b]);

            // When all the LHands are the same, we can avoid a SubStateMachine nesting
            // and collapse everything into just single nodes for RHand states.
            // Similarly, when all RHands are the same for a given LHand, we can
            // just make the RHand node a single state instead of a SubStateMachine.
            bool allLHandsAreSame = (uniqueLHandGestures.Length == 1);
            bool allRHandsAreSameForThisLHand = (uniqueRHandGestures.Length == 1);

            if (allLHandsAreSame && allRHandsAreSameForThisLHand) {
                // Shouldn't be possible because it implies there are no gestures except
                // neutral, and if that were the case, this entire function shouldn't
                // have been called.
                throw new Exception("Unexpected state - allLHandsAreSame and allRHandsAreSameForThisLHand are both true");
            }

            AacFlStateMachine smLHand;
            if (allLHandsAreSame || allRHandsAreSameForThisLHand) {
                smLHand = smRoot;
            }
            else {
                smLHand = smRoot.NewSubStateMachine(nameForGestureState(null, "L", lCopyIndices));
                all_sLHands[i] = smLHand;

                if (i == 0 && pauseHandGesturesParam != null) {
                    smLHand.TransitionsFromEntry()
                        .When(pauseHandGesturesParam.IsTrue());
                }

                if (uniqueLHandGestures.Length > 1) {
                    generateEntryTransitionsForState(smLHand, lCopyIndices, leftParamName, i==0);
                }
                smLHand.Exits();
            }

            AacAnimatorNode[] all_sRHands = new AacAnimatorNode[uniqueRHandGestures.Length];
            bool allSameEyesClosed = true;
            bool eyesClosedExpect = gestureMotionsEyesClosed[lGestureIndex, uniqueRHandGestures[0][0]]; // Common eyes-closed value, if all are the same
            for (int j = 0; j < uniqueRHandGestures.Length; j++) {
                // See description for "lCopyIndices" above. By definition,
                // gestureMotion[Y, rCopyIndices[X]] for any Y are all the same for all X.
                int[] rCopyIndices = uniqueRHandGestures[j];
                int rGestureIndex = rCopyIndices[0];

                Motion motion = gestureMotions[lGestureIndex, rGestureIndex];
                string rStateName;
                if (allLHandsAreSame) {
                    rStateName = nameForGestureState(null, "R", rCopyIndices);
                }
                else if (allRHandsAreSameForThisLHand) {
                    rStateName = nameForGestureState(null, "L", lCopyIndices);
                }
                else {
                    rStateName = nameForGestureState("L"+gestureParamValues[lGestureIndex], "R", rCopyIndices);
                }

                // The "substates" array is to facilitate the low-priority contact
                // expressions, which act like normal gesture states but actually
                // generate a whole state machine. In those cases (described below)
                // sRHand will be the state machine and sRHandSubstates will be the
                // array of contained states. Otherwise, for a normal gesture state,
                // sRHand == sRHandSubstates[0] and sRHandSubstates.Length == 1.
                //
                // A set of extension functions near the bottom of the file allows
                // many of the Aac functions, e.g. Exits() and WhenConditions() to
                // work in "parallel" on an array of states/transitions, which makes
                // the code here a bit easier to understand: just treat AacFlState[]
                // as if it were a single state.
                AacAnimatorNode sRHand;
                AacFlState[] sRHandSubstates;

                // If we have any low-priority contact expressions, any gesture that just
                // shows the neutral expression gets replaced by a state machine to switch
                // between neutral and the various contact expression states.
                if (contactMotionsLP.Length > 0 && motion == gestureMotions[0, 0]) {
                    sRHand = generateLPContactExpressionsSM(
                        smLHand, rStateName, out sRHandSubstates);

                    if (contactMotionsLPeyesClosedCanBeGrouped) {
                        if (gestureMotionsEyesClosed[0,0] != eyesClosedExpect) {
                            allSameEyesClosed = false;
                        }
                    }
                    else {
                        allSameEyesClosed = false;
                    }
                }
                // Normal gesture state
                else {
                    var s = smLHand.NewState(rStateName);
                    s.WithAnimation(motion);
                    sRHand = s;
                    sRHandSubstates = new AacFlState[] { s };

                    if (gestureMotionsEyesClosed[lGestureIndex, rGestureIndex] != eyesClosedExpect) {
                        allSameEyesClosed = false;
                    }
                }

                // When there's only one RHand gesture for an LHand gesture,
                // no SubStateMachine for the LHand is created, so the sRHand
                // needs to be put in its place for all_sLHands.
                if ( allRHandsAreSameForThisLHand ||
                     (allLHandsAreSame && j == 0)) {
                    all_sLHands[i] = sRHand;
                }
                all_sRHands[j] = sRHand;

                if (i == 0 && j == 0 && pauseHandGesturesParam != null) {
                    sRHand.TransitionsFromEntry()
                        .When(pauseHandGesturesParam.IsTrue());
                }

                if (allRHandsAreSameForThisLHand) {
                    // We're not in a LHand SubStateMachine, so make entry transitions
                    // directly from the root state machine.
                    generateEntryTransitionsForState(sRHand, lCopyIndices, leftParamName, i==0);
                } else {
                    generateEntryTransitionsForState(sRHand, rCopyIndices, rightParamName, j==0);
                }

                var lExitCond = generateExitTransitionsForState(sRHandSubstates, rCopyIndices, rightParamName, j==0);
                var rExitCond = generateExitTransitionsForState(sRHandSubstates, lCopyIndices, leftParamName, i==0);

                if (pauseHandGesturesParam != null) {
                    // For the "true neutral" state, don't exit (based on the hand
                    // gestures) unless pause hand gestures is off.
                    if (i == 0 && j == 0) {
                        if (lExitCond != null) {
                            lExitCond.And(pauseHandGesturesParam.IsFalse());
                        }
                        if (rExitCond != null) {
                            rExitCond.And(pauseHandGesturesParam.IsFalse());
                        }
                    }
                    // Exit if pause hand gesture
                    else {
                        exitState(sRHandSubstates).When(pauseHandGesturesParam.IsTrue());
                    }
                }

                // If there are manual overrides, add in the exit condition
                if (manualOverrideCondition != null) {
                    exitState(sRHandSubstates).When(manualOverrideCondition);
                }

                // If there are contact expressions, add the in the exit transitions for them
                foreach (var cond in contactExpressionConditions) {
                    var t = exitState(sRHandSubstates).When(cond);
                    if (pauseContactExpressionsParam != null) {
                        t.And(pauseContactExpressionsParam.IsFalse());
                    }
                }
            }

            if (anyEyesClosedMotions) {
                // Put the Tracking behavior on the LHand state machine if all the
                // contained states have the same eyes closed value. This just reduces
                // the number of behaviors, which is nice.
                if (allSameEyesClosed && !allRHandsAreSameForThisLHand) {
                    SetStateEyesClosed(smLHand, true, eyesClosedExpect);
                }
                else {
                    for (int j = 0; j < uniqueRHandGestures.Length; j++) {
                        int rGestureIndex = uniqueRHandGestures[j][0];
                        AacAnimatorNode state = all_sRHands[j];

                        if (state is AacFlStateMachine) { // state is a LP contact expression state machine
                            if (contactMotionsLPeyesClosedCanBeGrouped) {
                                // Put it on the state machine node
                                SetStateEyesClosed(state as AacFlStateMachine, true, gestureMotionsEyesClosed[0,0]);
                            }
                            else {
                                // Nothing to do; the behaviors will have already
                                // been put on the substates.
                            }
                        }
                        else { // Normal state
                            SetStateEyesClosed(state as AacFlState, true, gestureMotionsEyesClosed[lGestureIndex, rGestureIndex]);
                        }
                    }
                }
            }

            if (!(allLHandsAreSame || allRHandsAreSameForThisLHand)) {
                // Make a "catch all" transition to neutral, for gesture number 0,
                // as well as any disabled gestures. This works slightly differently
                // than the automatic default transition (orange line) because the
                // default transition will always jump straight to the innermost
                // state, instead of following Entry transitions on the statemachines.
                // We want it to follow entry transitions on the state machines.
                all_sRHands[0].TransitionsFromEntry();
            }
        }

        all_sLHands[0].TransitionsFromEntry();

        return smRoot;
    }

    // Finds the unique elements of an array and returns the index of the first
    // instance of each, as well as the indicies of all the duplicates of each.
    // The array is not passed directly; just its length and an `equal` compare
    // function.
    // For example if the array is:
    //   [A,B,A,C,C,B,D,B]
    //    0 1 2 3 4 5 6 7
    // the result is
    //   [[0,2], [1,5,7], [3,4], [6]]
    static int[][] FindUnique(int arrayLen, Func<int,int,bool> equal)
    {
        List<int[]> unique = new List<int[]>();
        bool[] isDup = new bool[arrayLen];
        for (int i = 0; i < arrayLen; i++) {
            if (!isDup[i]) {
                List<int> icopies = new List<int>();
                icopies.Add(i);
                for (int j = i+1; j < arrayLen; j++) {
                    if (equal(i, j)) {
                        isDup[j] = true;
                        icopies.Add(j);
                    }
                }
                unique.Add(icopies.ToArray());
            }
        }
        return unique.ToArray();
    }

#if USE_AAC_VRC_EXTENSIONS
    static public void SetStateEyesClosed<TNode>(TNode state, bool anyEyesClosedMotions, bool hasEyesClosed) where TNode : AacAnimatorNode<TNode>
    {
        if (anyEyesClosedMotions) {
            if (hasEyesClosed) {
                state.TrackingAnimates(AacAv3.Av3TrackingElement.Eyes);
            }
            else {
                state.TrackingTracks(AacAv3.Av3TrackingElement.Eyes);
            }
        }
    }
#else
    static public void SetStateEyesClosed(AacAnimatorNode n, bool x, bool y) { }
#endif

    static public void ClearPreviousAssets(MinimalGestureGenerator target)
    {
        AacConfiguration config = GetAACConfig(target);
        AacFlBase aac = AacV1.Create(config);
        aac.ClearPreviousAssets(); // Only clears assets with the given AssetKey
    }

    static public AacConfiguration GetAACConfig(MinimalGestureGenerator target)
    {
        string assetKeyName = String.IsNullOrEmpty(target.AssetKeyName)
            ? target.AvatarRoot.gameObject.name : target.AssetKeyName;
        string layerName = String.IsNullOrEmpty(target.LayerName)
            ? MinimalGestureGenerator.DefaultLayerName : target.LayerName;

        if (target.ComboGestureMode == MGGUtil.ComboGestureMode.LeftOnly) {
            assetKeyName += "_L";
            layerName += " Left";
        }
        else if (target.ComboGestureMode == MGGUtil.ComboGestureMode.RightOnly) {
            assetKeyName += "_R";
            layerName += " Right";
        }

        AacConfiguration config = new AacConfiguration {
            SystemName = layerName,
            AssetKey = assetKeyName,
            AnimatorRoot = target.AvatarRoot.transform,
            DefaultValueRoot = target.AvatarRoot.transform,
            AssetContainer = target.AssetContainer,
            DefaultsProvider = new AacDefaultsProvider(target.UseWriteDefaults),
        };

        return config;
    }

    // Merges a and b and returns the generated motion which must be saved to
    // the asset container. 'a' can be a clip or a blend tree.
    static public Motion MergeClips(Motion a, AnimationClip b)
    {
        Motion[] motions = new Motion[1];
        motions[0] = a;

        Motion[] newMotions;
        Motion[] generatedMotions;
        Motion newNeutral;
        EditorCurveBinding[] missingBindings;
        AnimationClip allBindingsClip;

        bool OK = RecreateMotionsToHaveAllTheSameAnimatedProperties(
            motions,
            b,
            null,
            out newMotions,
            out generatedMotions,
            out newNeutral,
            out missingBindings,
            out allBindingsClip,
            false);

        if (!OK) {
            // I don't think it should be possible for RecreateMotionsToHaveAllTheSameAnimatedProperties
            // to fail when used in this way.
            throw new Exception("Fail in MergeClips");
        }

        return newMotions[0];
    }

    // Acts like RecreateClipsToHaveAllTheSameAnimatedProperties except this
    // supports Motion arrays containing BlendTrees. Also, 'generatedMotions'
    // output will include the 'newNeutral'. Otherwise the arguments work the
    // same way.
    static public bool RecreateMotionsToHaveAllTheSameAnimatedProperties(
        Motion[] motions,
        Motion neutral,
        GameObject root,
        out Motion[] newMotions,
        out Motion[] generatedMotions,
        out Motion newNeutral,
        out EditorCurveBinding[] missingBindings,
        out AnimationClip allBindingsClip,
        bool fillInNullMotionsInBlendTree = true)
    {
        var clips = new List<AnimationClip>();
        var toplevelBlendTrees = new List<BlendTree>();
        var toplevelBlendTreeIndices = new List<int>();

        if (neutral != null && !(neutral is AnimationClip || neutral is BlendTree)) {
            throw new Exception($"Unknown Motion subclass {neutral}.");
        }

        for (int i = 0; i < motions.Length; i++) {
            if (motions[i] == null) {
                clips.Add(null);
            } else if (motions[i] is BlendTree) {
                toplevelBlendTrees.Add(motions[i] as BlendTree);
                toplevelBlendTreeIndices.Add(i);
                clips.Add(null);
            } else if (motions[i] is AnimationClip) {
                clips.Add(motions[i] as AnimationClip);
            } else {
                throw new Exception($"Unknown Motion subclass {motions[i]}.");
            }
        }

        bool neutralIsBlendTree = (neutral != null && neutral is BlendTree);

        // Collect all the blend trees, recursively
        var allBlendTrees = new HashSet<BlendTree>(toplevelBlendTrees);
        if (neutralIsBlendTree) {
            allBlendTrees.Add(neutral as BlendTree);
        }
        foreach(BlendTree bt in toplevelBlendTrees) {
            FindChildBlendTrees(bt, allBlendTrees);
        }

        // Collect all the animation clips in all the blend trees
        var allClipsInBlendTrees = new HashSet<AnimationClip>();
        foreach(BlendTree bt in allBlendTrees) {
            foreach(ChildMotion cm in bt.children) {
                if (cm.motion == null) {
                    ;
                } else if (cm.motion is BlendTree) {
                    ;
                } else if (cm.motion is AnimationClip) {
                    allClipsInBlendTrees.Add(cm.motion as AnimationClip);
                } else {
                    throw new Exception($"Unknown Motion subclass {cm.motion} in Blend Tree {bt.name}.");
                }
            }
        }

        // Add all the clips from the blend trees into the one big clips array
        // that's passed to RecreateClipsToHaveAllTheSameAnimatedProperties.
        clips.AddRange(allClipsInBlendTrees);

        // If the user specified a BlendTree for netural, 'neutralClip' will be
        // null and the defaultnull and the default values for neutral will only
        // come from the 'root' GameObject hierarchy.
        AnimationClip neutralClip = neutral as AnimationClip;

        // Do the real work for all the clips!
        AnimationClip[] newClips, generatedClips;
        AnimationClip newNeutralClip;
        bool OK = RecreateClipsToHaveAllTheSameAnimatedProperties(
            clips.ToArray(), neutralClip, root, out newClips, out generatedClips, out newNeutralClip, out missingBindings);
        if (!OK) {
            newMotions = null;
            generatedMotions = null;
            newNeutral = null;
            allBindingsClip = null;
            return false;
        }
        allBindingsClip = newNeutralClip;

        // Create mapping from old to new blend tree clips
        // The old blend tree clips are in the 'clips' list in the range [motions.Length, clips.Count)
        // And the new blend tree clips should be in the same range in the 'newClips' array
        var oldToNewBlendTreeClipsMap = new Dictionary<AnimationClip, AnimationClip>();
        for (int i = motions.Length; i < clips.Count; i++) {
            oldToNewBlendTreeClipsMap[clips[i]] = newClips[i];
        }

        // Make a copy of all the original blend tree objects
        var newAllBlendTrees = new BlendTree[allBlendTrees.Count];
        var oldToNewBlendTreeMap = new Dictionary<BlendTree, BlendTree>();
        int ix = 0;
        foreach(BlendTree bt in allBlendTrees) {
            newAllBlendTrees[ix] = ShallowCopyBlendTree(bt);
            oldToNewBlendTreeMap[bt] = newAllBlendTrees[ix];
            ix++;
        }

        // Replace all blend tree references with new blend tree objects
        // And replace all the clip references with the new clip objects
        // Don't fill in null references yet
        foreach(BlendTree bt in newAllBlendTrees) {
            // bt.children returns a copied array, so we can't directly modify the elements.
            // Also ChildMotion is a struct, so we can't do `ChildMotion cm = children[i]`
            // and modify that, because `cm` will be a copy.
            ChildMotion[] cms = bt.children;
            for (int i = 0; i < cms.Length; i++) {
                if (cms[i].motion == null) {
                    ;
                } else if (cms[i].motion is BlendTree) {
                    cms[i].motion = oldToNewBlendTreeMap[cms[i].motion as BlendTree];
                } else if (cms[i].motion is AnimationClip) {
                    cms[i].motion = oldToNewBlendTreeClipsMap[cms[i].motion as AnimationClip];
                }
            }
            bt.children = cms;
        }

        if (neutralIsBlendTree) {
            BlendTree newNeutralBlendTree = oldToNewBlendTreeMap[neutral as BlendTree];
            newNeutral = newNeutralBlendTree as Motion;

            if (fillInNullMotionsInBlendTree) {
                // When the neutral motion is a blend tree, find all the blend trees
                // referenced by it and replace any null motions with the newNeutralClip.
                // We don't want to replace null motions in these with the new neutral
                // motion BlendTree because that would make a circular reference!
                var blendTreesUsedByNeutral = new HashSet<BlendTree>();
                blendTreesUsedByNeutral.Add(newNeutralBlendTree);
                FindChildBlendTrees(newNeutralBlendTree, blendTreesUsedByNeutral);
                foreach(BlendTree bt in blendTreesUsedByNeutral) {
                    ChildMotion[] cms = bt.children;
                    for (int i = 0; i < cms.Length; i++) {
                        if (cms[i].motion == null) {
                            cms[i].motion = newNeutralClip;
                        }
                    }
                    bt.children = cms;
                }
            }

            // Now it's safe to replace all remaining null motions in
            // the blend trees with the new netural motion, below.
        }
        else {
            newNeutral = newNeutralClip as Motion;
        }

        // Replace nulls in blend trees with newNeutral
        if (fillInNullMotionsInBlendTree) {
            foreach(BlendTree bt in newAllBlendTrees) {
                ChildMotion[] cms = bt.children;
                for (int i = 0; i < cms.Length; i++) {
                    if (cms[i].motion == null) {
                        cms[i].motion = newNeutral;
                    }
                }
                bt.children = cms;
            }
        }

        // Create newMotions array using the new clips and new blend trees
        newMotions = new Motion[motions.Length];
        for (int i = 0; i < motions.Length; i++) {
            if (motions[i] == null) {
                newMotions[i] = newNeutral;
            } else if (motions[i] is BlendTree) {
                newMotions[i] = oldToNewBlendTreeMap[motions[i] as BlendTree] as Motion;
            } else if (motions[i] is AnimationClip) {
                newMotions[i] = newClips[i] as Motion;
            } else {
                throw new Exception($"Unknown Motion subclass {motions[i]}.");
            }
        }

        // And finally create new generatedMotions list consisting off
        // all the generated clips and all the generated blend trees.
        generatedMotions = (generatedClips as Motion[]).Concat(newAllBlendTrees).Append(newNeutralClip as Motion).ToArray();

        return true;
    }

    static public void FindChildBlendTrees(
        BlendTree bt, // input
        HashSet<BlendTree> allBlendTrees) // output
    {
        foreach(ChildMotion cm in bt.children) {
            if (cm.motion != null && cm.motion is BlendTree) {
                BlendTree cbt = cm.motion as BlendTree;
                if (allBlendTrees.Add(cbt)) {
                    FindChildBlendTrees(cbt, allBlendTrees);
                }
            }
        }
    }

    // I thought Instantiate would do this but it was giving weird errors.
    static public BlendTree ShallowCopyBlendTree(BlendTree bt)
    {
        BlendTree nbt = new BlendTree();
        nbt.blendParameter = bt.blendParameter;
        nbt.blendParameterY = bt.blendParameterY;
        nbt.blendType = bt.blendType;
        nbt.children = bt.children; // bt.children returns a copy, so changes to nbt.children won't modify the original here
        nbt.maxThreshold = bt.maxThreshold;
        nbt.minThreshold = bt.minThreshold;
        nbt.useAutomaticThresholds = bt.useAutomaticThresholds;
        nbt.name = bt.name;
        return nbt;
    }

    static public readonly EditorCurveBindingComparer bindingComparer = new EditorCurveBindingComparer();

    // Given an array of AnimationClips, finds the union of all animated properties
    // on all clips, and outputs a new array of AnimationClips where each clip has
    // all the animated properties of that union. That is to say, all output clips
    // will animate the same set of proeprties. This allows the clips to transition
    // between each other without needing Write Defaults on.
    //
    // When filling in a missing animated property in a clip,
    //   1. If the property is present in 'neutral', the animation curve for that
    //      property is copied from 'neutral' into the new clip.
    //   2. Otherwise, if 'neutral' is null or doesn't animate that property,
    //      the animated property is looked up in the GameObject hierarchy of 'root'.
    //      The current value of the property is used to generate a new animation curve.
    //   3. Otherwise, if 'root' is null or doesn't have the property, false is
    //      returned and missingBindings is set to the missing properties.
    //
    // Either neutral or root must not be null.
    //
    // An output neutral clip is also generated with the same common bindings set,
    // even if no 'neutral' clip was provided.
    //
    // Output 'generatedClips' contains a no-duplicates list of the newly generated
    // AnimationClips which must be saved to the asset container. Not including
    // 'newNeutral', which must be saved too.
    static public bool RecreateClipsToHaveAllTheSameAnimatedProperties(
        AnimationClip[] clips,
        AnimationClip neutral,
        GameObject root,
        out AnimationClip[] newClips,
        out AnimationClip[] generatedClips,
        out AnimationClip newNeutral,
        out EditorCurveBinding[] missingBindings)
    {
        newClips = null;
        generatedClips = null;
        newNeutral = null;
        missingBindings = null;

        AnimationClip[] clipsNoDups = clips
            .Where(a => a != null && a != neutral)
            .Distinct()
            .ToArray();

        // Get the bindings used by all clips (don't include neutral because
        // it might have extra bindings that aren't used by any actual clips)
        var allBindings = GetAllBindingsFromClips(clipsNoDups);

        if (allBindings.Count() == 0 && neutral != null) {
            allBindings = GetAllBindingsFromClip(neutral);
        }

        AnimationClip defaults =
            root == null
                ? new AnimationClip()
                : GenerateDefaultsClipFromHierarchy(root, allBindings);
        defaults.name = $"AutoNeutral";

        // Create new neutral clip with all bindings, filling in defaults from 'root' hierarchy
        bool OK = true;
        AnimationClip neutralAllBindings =
            neutral == null
                ? defaults
                : RecreateClipWithNewBindings(neutral, defaults, allBindings, ref OK);

        missingBindings = allBindings.Except(GetAllBindingsFromClip(neutralAllBindings), bindingComparer).ToArray();
        if (missingBindings.Length > 0) {
            return false;
        }

        if (!OK) {
            // I don't think this should happen. If anything was missing,
            // it should have been caught by missingBindings check above.
            throw new Exception("Unexpectedly missing bindings when generating all-bindings Neutral clip.");
        }

        // Make new animation clips that have all bindings from allBindings
        AnimationClip[] clipsNoDupsAllBindings = new AnimationClip[clipsNoDups.Length];
        for (int i = 0; i < clipsNoDups.Length; i++) {
            OK = true;
            clipsNoDupsAllBindings[i] = RecreateClipWithNewBindings(clipsNoDups[i], neutralAllBindings, allBindings, ref OK);
            if (!OK) {
                // Same here, I think this shouldn't happen.
                throw new Exception($"Unexpectedly missing bindings when generating all-bindings clip for {clipsNoDups[i].name}.");
            }
        }

        // Recreate the original clips array but using the deduped-allbindings versions
        AnimationClip[] clipsAllBindings = new AnimationClip[clips.Length];
        for (int i = 0; i < clips.Length; i++) {
            if (clips[i] == null || clips[i] == neutral) {
                clipsAllBindings[i] = neutralAllBindings;
            }
            else {
                int noDupsIndex = Array.IndexOf(clipsNoDups, clips[i]);
                clipsAllBindings[i] = clipsNoDupsAllBindings[noDupsIndex];
            }
        }

        newClips = clipsAllBindings;
        generatedClips = clipsNoDupsAllBindings;
        newNeutral = neutralAllBindings;
        return true;
    }

    static AnimationClip GenerateDefaultsClipFromHierarchy(GameObject root, IEnumerable<EditorCurveBinding> bindings)
    {
        AnimationClip clip = new AnimationClip();
        foreach (EditorCurveBinding binding in bindings) {
            if (binding.isPPtrCurve) {
                UnityEngine.Object value;
                if (AnimationUtility.GetObjectReferenceValue(root, binding, out value)) {
                    ObjectReferenceKeyframe[] curve = new ObjectReferenceKeyframe[] {
                        new ObjectReferenceKeyframe {
                            time = 0,
                            value = value,
                        }
                    };
                    AnimationUtility.SetObjectReferenceCurve(clip, binding, curve);
                }
            }
            else {
                float value;
                if (AnimationUtility.GetFloatValue(root, binding, out value)) {
                    AnimationCurve curve = new AnimationCurve();
                    curve.AddKey(0, value);
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
            }
        }
        return clip;
    }

    // Creates a copy of 'source' using the bindings from 'newBindings'.
    // The animation curves will be taken from 'source' if present, otherwise from 'defaults'.
    // If an animation curve is not present in either, it will be skipped and OK will be set false.
    // If a curve is a single keyframe at time 0, it will be turned into two keyframes.
    static public AnimationClip RecreateClipWithNewBindings(AnimationClip source, AnimationClip defaults, IEnumerable<EditorCurveBinding> newBindings, ref bool OK)
    {
        EditorCurveBinding[] sourceBindings = GetAllBindingsFromClip(source).ToArray();
        EditorCurveBinding[] defaultsBindings = defaults != null ? GetAllBindingsFromClip(defaults).ToArray() : null;
        AnimationClip newClip = new AnimationClip();
        newClip.name = source.name;
        var settings = AnimationUtility.GetAnimationClipSettings(source);
        AnimationUtility.SetAnimationClipSettings(newClip, settings);
        foreach (EditorCurveBinding binding in newBindings) {
            if (binding.isPPtrCurve) {
                ObjectReferenceKeyframe[] curve = null;
                if (sourceBindings.Contains(binding, bindingComparer)) {
                    curve = AnimationUtility.GetObjectReferenceCurve(source, binding);
                }
                else if (defaultsBindings != null && defaultsBindings.Contains(binding, bindingComparer)) {
                    curve = AnimationUtility.GetObjectReferenceCurve(defaults, binding);
                }
                else {
                    OK = false;
                    continue;
                }
                if (curve != null && curve.Length == 1 && curve[0].time == 0) {
                    ObjectReferenceKeyframe copy = new ObjectReferenceKeyframe {
                        time = 1f/source.frameRate,
                        value = curve[0].value,
                    };
                    curve = new ObjectReferenceKeyframe[] { curve[0], copy };
                }
                AnimationUtility.SetObjectReferenceCurve(newClip, binding, curve);
            }
            else {
                AnimationCurve curve = null;
                if (sourceBindings.Contains(binding, bindingComparer)) {
                    curve = AnimationUtility.GetEditorCurve(source, binding);
                }
                else if (defaultsBindings != null && defaultsBindings.Contains(binding, bindingComparer)) {
                    curve = AnimationUtility.GetEditorCurve(defaults, binding);
                }
                else {
                    OK = false;
                    continue;
                }
                if (curve != null && curve.keys.Length == 1 && curve.keys[0].time == 0) {
                    curve.AddKey(1f/source.frameRate, curve.keys[0].value);
                }
                AnimationUtility.SetEditorCurve(newClip, binding, curve);
            }
        }
        return newClip;
    }

    // Gets a union of all bindings in the given clips
    static public IEnumerable<EditorCurveBinding> GetAllBindingsFromClips(AnimationClip[] clips)
    {
        IEnumerable<EditorCurveBinding> allBindings = Enumerable.Empty<EditorCurveBinding>();
        foreach (AnimationClip clip in clips) {
            if (clip == null) { continue; }
            allBindings = allBindings.Union(GetAllBindingsFromClip(clip), bindingComparer);
        }
        return allBindings;
    }

    static public IEnumerable<EditorCurveBinding> GetAllBindingsFromClip(AnimationClip clip)
    {
        return AnimationUtility.GetObjectReferenceCurveBindings(clip).Union(AnimationUtility.GetCurveBindings(clip), bindingComparer);
    }

    static public bool EditorCurveBindingsEqual(EditorCurveBinding a, EditorCurveBinding b)
    {
        return a.propertyName == b.propertyName && a.path == b.path;
    }

    public class EditorCurveBindingComparer : IEqualityComparer<EditorCurveBinding>
    {
        public bool Equals(EditorCurveBinding a, EditorCurveBinding b)
        {
            return EditorCurveBindingsEqual(a, b);
        }

        public int GetHashCode(EditorCurveBinding obj)
        {
            return obj.propertyName.GetHashCode() ^ obj.type.GetHashCode() ^ obj.path.GetHashCode();
        }
    }

    // Assumes the clip doesn't animate any humanoid body parts, since the
    // avatar's FX layer shouldn't be doing that anyway! The user can put
    // in their own mask if needed.
    static public AvatarMask GenerateAvatarMaskForClip(AnimationClip clip)
    {
        AvatarMask mask = new AvatarMask();

        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) {
            mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
        }

        string[] paths = GetAffectedTransformPathsForClip(clip);

        if (paths.Length > 0) {
            mask.transformCount = paths.Length;
            for (int i = 0; i < paths.Length; i++) {
                mask.SetTransformPath(i, paths[i]);
                mask.SetTransformActive(i, true);
            }
        }
        else {
            // A completely empty mask has no effect; it won't mask any transforms.
            // Add at least one transform so that the mask actually works.
            mask.transformCount = 1;
            mask.SetTransformPath(0, "_ignored");
            mask.SetTransformActive(0, false);
        }

        return mask;
    }

    static public string[] GetAffectedTransformPathsForClip(AnimationClip clip)
    {
        IEnumerable<Transform> allBindings = Enumerable.Empty<Transform>();
        var objRefBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        var floatBindings = AnimationUtility.GetCurveBindings(clip);
        return objRefBindings.Select(b => b.path)
            .Union(floatBindings.Where(b => b.type == typeof(Transform)).Select(b => b.path))
            .Distinct()
            .ToArray();
    }
}

static internal class MMG_AacExtensions
{
    // static public AacFlNewTransitionContinuation Exits(this AacAnimatorNode node)
    // {
    //     if (node is AacFlState) {
    //         return (node as AacFlState).Exits();
    //     } else if (node is AacFlStateMachine) {
    //         return (node as AacFlStateMachine).Exits();
    //     } else {
    //         throw new ArgumentException("node must be AacFlState or AacFlStateMachine");
    //     }
    // }

    static public AacFlEntryTransition EntryTransitionsTo(this AacFlStateMachine sm, AacAnimatorNode target)
    {
        if (target is AacFlState) {
            return sm.EntryTransitionsTo(target as AacFlState);
        } else if (target is AacFlStateMachine) {
            return sm.EntryTransitionsTo(target as AacFlStateMachine);
        } else {
            throw new ArgumentException("target must be AacFlState or AacFlStateMachine");
        }
    }

    static public AacFlEntryTransition TransitionsFromEntry(this AacAnimatorNode node)
    {
        if (node is AacFlState) {
            return (node as AacFlState).TransitionsFromEntry();
        } else if (node is AacFlStateMachine) {
            return (node as AacFlStateMachine).TransitionsFromEntry();
        } else {
            throw new ArgumentException("node must be AacFlState or AacFlStateMachine");
        }
    }

    static public AacFlNewTransitionContinuation TransitionsTo(this AacFlStateMachine sm, AacAnimatorNode target)
    {
        if (target is AacFlState) {
            return sm.TransitionsTo(target as AacFlState);
        } else if (target is AacFlStateMachine) {
            return sm.TransitionsTo(target as AacFlStateMachine);
        } else {
            throw new ArgumentException("target must be AacFlState or AacFlStateMachine");
        }
    }

    static public AacFlTransition TransitionsTo(this AacFlState s, AacAnimatorNode target)
    {
        if (target is AacFlState) {
            return s.TransitionsTo(target as AacFlState);
        } else if (target is AacFlStateMachine) {
            return s.TransitionsTo(target as AacFlStateMachine);
        } else {
            throw new ArgumentException("target must be AacFlState or AacFlStateMachine");
        }
    }

    static public AacFlNewTransitionContinuation TransitionsTo(this AacAnimatorNode s, AacAnimatorNode target)
    {
        if (s is AacFlState) {
            if (target is AacFlState) {
                return (s as AacFlState).TransitionsTo(target as AacFlState);
            } else if (target is AacFlStateMachine) {
                return (s as AacFlState).TransitionsTo(target as AacFlStateMachine);
            } else {
                throw new ArgumentException("target must be AacFlState or AacFlStateMachine");
            }
        }
        else if (s is AacFlStateMachine) {
            if (target is AacFlState) {
                return (s as AacFlStateMachine).TransitionsTo(target as AacFlState);
            } else if (target is AacFlStateMachine) {
                return (s as AacFlStateMachine).TransitionsTo(target as AacFlStateMachine);
            } else {
                throw new ArgumentException("target must be AacFlState or AacFlStateMachine");
            }
        }
        else {
            throw new ArgumentException("s must be AacFlState or AacFlStateMachine");
        }
    }

    static public AacFlTransition WithConditionalDestinationInterruption(this AacFlTransition t, bool doInterruption)
    {
        return doInterruption ? t.WithInterruption(TransitionInterruptionSource.Destination) : t;
    }


    // Array extensions - allows adding transitions to multiple states at once.
    // I've only implemented the functions needed for the existing code here.

    static public AacFlTransition[] Exits(this AacFlState[] states)
    {
        return states.Select(s => s.Exits()).ToArray();
    }

    static public AacFlTransition[] WithTransitionDurationSeconds(this AacFlTransition[] transitions, float duration)
    {
        return transitions.Select(t => t.WithTransitionDurationSeconds(duration)).ToArray();
    }

    static public AacFlTransition[] WithConditionalDestinationInterruption(this AacFlTransition[] transitions, bool doInterruption)
    {
        return transitions.Select(t => t.WithConditionalDestinationInterruption(doInterruption)).ToArray();
    }

    static public AacFlTransitionContinuation[] When(this AacFlNewTransitionContinuation[] transitions, IAacFlCondition action)
    {
        return transitions.Select(t => t.When(action)).ToArray();
    }

    static public AacFlTransitionContinuation[] WhenConditions(this AacFlNewTransitionContinuation[] transitions)
    {
        return transitions.Select(t => t.WhenConditions()).ToArray();
    }

    static public AacFlTransitionContinuation[] And(this AacFlTransitionContinuation[] transitions, IAacFlCondition action)
    {
        return transitions.Select(t => t.And(action)).ToArray();
    }
}

static public class MGGAnimatorUtils
{
    // Clears all the contents of a layer, making it like it was newly-created.
    //
    // Correctly clearing all the contents of an animator layer is surprisingly
    // difficult, even Unity gets it wrong. There are a number of approaches to
    // do it, but the way Unity provides directly with functions like 'RemoveLayer'
    // or 'RemoveStateMachine' can be very slow (10s of seconds) or leak assets
    // in the .controller file (leaving unused assets to never get deleted.)
    //
    // Here are some methods and their drawbacks:
    //  1. Calling 'RemoveLayer' on the layer and then adding a new one.
    //     This is easy, but isn't very fast (only a little faster than method
    //     2 in my tests) and it also makes the Animator GUI freak out and spew
    //     errors if you're looking at the layer that was removed. So it's best
    //     to clear the layer in place. It also leaks some assets, more on that
    //     later.
    //
    //  2. On the layer's root StateMachine, using the 'RemoveAnyStateTransition',
    //     'RemoveEntryTransition', 'RemoveState', 'RemoveStateMachine', and
    //     'RemoveStateMachineTransition' functions to remove all the objects.
    //     This is presumably the Unity-"recommended" way to do it, but it's
    //     slow and still some leaks assets. It took over 30 seconds on layer
    //     with a lot of states.
    //     NOTE: 'RemoveStateMachine' is automatically recursive and clears all
    //     the contents of sub-StateMachines for you, so there's no need to
    //     recurse into sub-StateMachines with this method. If you try, all the
    //     extra 'Remove*' calls make things _much_ slower.
    //     Unfortunately the layer's root StateMachine can't be removed with
    //     'RemoveStateMachine'.
    //
    //  3. Using C# Reflection to call the internal 'Clear' method of the root
    //     state machine. This is what Unity does internally to destroy the assets
    //     from the layer when calling 'RemoveLayer', so it has essentially the
    //     same drawbacks as method 1, except it doesn't cause the Animator GUI
    //     to spew errors. Example code to do this:
    //         Type t = typeof(AnimatorStateMachine);
    //         var clearMethod = t.GetMethod("Clear",
    //             BindingFlags.Instance | BindingFlags.NonPublic);
    //         clearMethod.Invoke(rootsm, null);
    //
    //  4. All the previous methods will leak some assets. Specifically, "state
    //     machine transitions" (AnimatorTransitions from a sub-StateMachine to
    //     an Exit or another state) will not be deleted from the .controller
    //     file, and will build up over time, wasting file space.
    //     To work around this Unity bug, we have to delete those transition
    //     assets manually before clearing the layer. One way is to traverse the
    //     layer recursively, calling GetStateMachineTransitions and removing
    //     them. Then we can use any of the other methods above to clear out
    //     everything else, avoiding all leaks.
    //
    //  5. In order to make things faster, we can use DestroyImmediate instead
    //     of the "recommended" functions for removing objects in the layer.
    //     That's the method this function uses. It recursively collects all
    //     the objects used by the layer, and then manually deletes them.
    //     It avoids asset leaks, and is orders of magnitude faster (only takes
    //     a few milliseconds!)
    //
    //  6. A more comprehensive solution to removing leaked assets can be to
    //     find all the used objects like this function, but across all layers,
    //     then use AssetDatabase.LoadAllAssetsAtPath to find all the assets in
    //     the file. Then any assets in the file that aren't used can be deleted.
    //     That solves the problem even for leaked assets created by the Animator
    //     GUI, but isn't really in the scope of what this tool should do to the
    //     provided animator file.
    //
    static public void ClearLayer(AnimatorController controller, AnimatorControllerLayer layer)
    {
        // Modifying these properties on the layer directly doesn't work because
        // controller.layers returns a copy. Make the change and then reassign
        // the array back to controller.layers.
        var layers = controller.layers;
        bool found = false;
        foreach (var l in layers) {
            if (l.name == layer.name) {
                l.avatarMask = null;
                l.blendingMode = AnimatorLayerBlendingMode.Override;
                l.defaultWeight = 1;
                l.iKPass = false;
                l.syncedLayerAffectsTiming = false;
                l.syncedLayerIndex = -1;
                found = true;
                break;
            }
        }

        if (!found) {
            throw new ArgumentException($"The given layer '{layer.name}' is not a layer in the animator controller '{controller.name}'");
        }

        controller.layers = layers;

        AnimatorStateMachine rootsm = layer.stateMachine;
        string controllerAssetPath = AssetDatabase.GetAssetPath(controller);

        if (String.IsNullOrEmpty(controllerAssetPath)) {
            // Don't try to remove "subassets" if the controller isn't a saved asset.
            // Just clear the arrays and we're done.
            rootsm.anyStateTransitions = new AnimatorStateTransition[0];
            rootsm.behaviours = new StateMachineBehaviour[0];
            rootsm.entryTransitions = new AnimatorTransition[0];
            rootsm.states = new ChildAnimatorState[0];
            rootsm.stateMachines = new ChildAnimatorStateMachine[0];
            return;
        }

        var assets = new HashSet<UnityEngine.Object>();
        var motions = new HashSet<Motion>();

        ///// Internal helper function /////
        void recursiveCollectAssets(AnimatorStateMachine sm)
        {
            assets.UnionWith(sm.entryTransitions);
            assets.UnionWith(sm.anyStateTransitions);
            assets.UnionWith(sm.behaviours);
            foreach (ChildAnimatorState cs in sm.states) {
                assets.Add(cs.state);
                assets.UnionWith(cs.state.transitions);
                assets.UnionWith(cs.state.behaviours);
                if (cs.state.motion != null) {
                    motions.Add(cs.state.motion);
                }
            }
            foreach (ChildAnimatorStateMachine csm in sm.stateMachines) {
                if (assets.Add(csm.stateMachine)) {
                    // There is no property to access these. They're so easy to forget
                    // about that it seems even Unity forgets about these! Deleting a
                    // layer in the Animator GUI will leak these StateMachine transitions
                    // in the asset file.
                    assets.UnionWith(sm.GetStateMachineTransitions(csm.stateMachine));
                    recursiveCollectAssets(csm.stateMachine);
                }
            }
        }
        ///// End internal helper function /////

        recursiveCollectAssets(rootsm);

        // Clear the arrays before destroying assets, otherwise Unity barfs
        rootsm.anyStateTransitions = new AnimatorStateTransition[0];
        rootsm.behaviours = new StateMachineBehaviour[0];
        rootsm.entryTransitions = new AnimatorTransition[0];
        rootsm.states = new ChildAnimatorState[0];
        rootsm.stateMachines = new ChildAnimatorStateMachine[0];

        ///// Internal helper function /////
        bool isOkToRemove(UnityEngine.Object o)
        {
            // Check that the object is hidden. We should assume any non-hidden
            // objects might have external users.
            if ((o.hideFlags & HideFlags.HideInHierarchy) != HideFlags.HideInHierarchy) {
                return false;
            }

            // Check that the object is contained in the controller asset file.
            // Checking if two objects have the same file path is apparently the
            // Unity-approved method for checking if one asset is contained in
            // another, according to code from UnityCsReference. Seems a bit weird
            // that this is the best way to do it.
            // https://github.com/Unity-Technologies/UnityCsReference/blob/2a49d60b87de8036523dcedcbae97398f64f5fb8/Editor/Mono/Animation/MecanimUtilities.cs#L32-L35
            return AssetDatabase.GetAssetPath(o) == controllerAssetPath;
        }
        ///// End internal helper function /////

        // Remove the subassets from the controller
        foreach (var asset in assets) {
            if (isOkToRemove(asset)) {
                UnityEngine.Object.DestroyImmediate(asset, /* allowDestroyingAssets = */ true);
            }
        }

        // ----- The reset here is to clear out Motions (AnimationClips and BlendTrees) -----
        // We need to be more careful with these, because it's more common for
        // these to be used across multiple layers. Again, this assumes that a
        // hidden Motion or BlendTree won't be used externally.

        ///// Internal helper function /////
        void recursiveCollectMotions(Motion m, HashSet<Motion> ms)
        {
            if (m != null && ms.Add(m)) {
                if (m is BlendTree) {
                    BlendTree bt = m as BlendTree;
                    foreach (ChildMotion cm in bt.children) {
                        recursiveCollectMotions(cm.motion, ms);
                    }
                }
            }
        }
        void recursiveCollectMotionsInSM(AnimatorStateMachine sm, HashSet<AnimatorStateMachine> sms, HashSet<Motion> ms)
        {
            foreach (ChildAnimatorState cs in sm.states) {
                recursiveCollectMotions(cs.state.motion, ms);
            }
            foreach (ChildAnimatorStateMachine csm in sm.stateMachines) {
                if (sms.Add(csm.stateMachine)) {
                    recursiveCollectMotionsInSM(csm.stateMachine, sms, ms);
                }
            }
        }
        ///// End internal helper function /////

        var potentialMotionsToRemove = motions.Where(o => isOkToRemove(o)).ToArray();

        if (potentialMotionsToRemove.Length > 0) {
            // This layer uses some AnimationClips or BlendTrees that are internal
            // to this controller. Before we delete them, check that they are not
            // used by other layers.

            var recursivePotentialMotionsToRemove = new HashSet<Motion>();
            foreach (Motion m in potentialMotionsToRemove) {
                recursiveCollectMotions(m, recursivePotentialMotionsToRemove);
            }
            var recursivePotentialMotionsToRemoveFiltered = recursivePotentialMotionsToRemove.Where(o => isOkToRemove(o));

            var recursiveMotionsUsedInOtherLayers = new HashSet<Motion>();
            var foundStateMachines = new HashSet<AnimatorStateMachine>();
            foreach (AnimatorControllerLayer l in controller.layers) {
                if (l.name != layer.name) {
                    recursiveCollectMotionsInSM(l.stateMachine, foundStateMachines, recursiveMotionsUsedInOtherLayers);
                }
            }

            var motionsToRemove =
                recursivePotentialMotionsToRemoveFiltered.Except(recursiveMotionsUsedInOtherLayers);
            foreach (var motion in motionsToRemove) {
                UnityEngine.Object.DestroyImmediate(motion, /* allowDestroyingAssets = */ true);
            }
        }
    }

    // Subassets frequently get left behind in AnimatorController asset files.
    // In particular both the Animator GUI and Unity's recommended functions
    // for removing objects from an animator layer (e.g. RemoveStateMachine,
    // RemoveLayer, etc) can leave behind AnimatorTransition assets in the
    // .controller file, never to be deleted.
    //
    // This function makes an attempt to clean up unused assets from an animator
    // controller. Most of the subassets are relatively straightforward to
    // remove: list all the assets in the file, walk all the objects in the
    // layers to find all the used assets, and the difference is the unused
    // assets which can be removed.
    //
    // However in the case of AnimationClips and BlendTrees, which are commonly
    // contained within AnimatorControllers, it's not as simple because it's
    // also not unusal for those assets to be used externally. E.g. a container
    // animator controller which holds assets used by other animator controllers.
    // This function just does not consider AnimationClips or BlendTrees.
    //
    // This function ended up not being used in MGG, but it's nice to have
    // for reference and I wanted to keep it around, so it's here. Enjoy, I
    // guess.
    static void RemoveUnusedAssetsFromAnimatorController(AnimatorController controller, bool logNumRemoved=false)
    {
        string path = AssetDatabase.GetAssetPath(controller);
        if (String.IsNullOrEmpty(path)) {
            return;
        }

        UnityEngine.Object[] allOriginalObjects = AssetDatabase.LoadAllAssetsAtPath(path);

        // Try to avoid deleting anything used externally by only deleting hidden assets
        UnityEngine.Object[] hiddenOriginalObjects =
            allOriginalObjects.Where(o => o != null && (o.hideFlags & HideFlags.HideInHierarchy) == HideFlags.HideInHierarchy).ToArray();

        var originalAnimatorStateMachines    = hiddenOriginalObjects.OfType<AnimatorStateMachine>().ToArray();
        var originalAnimatorTransitions      = hiddenOriginalObjects.OfType<AnimatorTransition>().ToArray();
        var originalAnimatorStateTransitions = hiddenOriginalObjects.OfType<AnimatorStateTransition>().ToArray();
        var originalAnimatorStates           = hiddenOriginalObjects.OfType<AnimatorState>().ToArray();
        var originalStateMachineBehaviours   = hiddenOriginalObjects.OfType<StateMachineBehaviour>().ToArray();

        var foundAnimatorStateMachines       = new HashSet<AnimatorStateMachine>();
        var foundAnimatorTransitions         = new HashSet<AnimatorTransition>();
        var foundAnimatorStateTransitions    = new HashSet<AnimatorStateTransition>();
        var foundAnimatorStates              = new HashSet<AnimatorState>();
        var foundStateMachineBehaviours      = new HashSet<StateMachineBehaviour>();

        void recursiveCollectAssets(AnimatorStateMachine sm)
        {
            foundAnimatorTransitions.UnionWith(sm.entryTransitions);
            foundAnimatorStateTransitions.UnionWith(sm.anyStateTransitions);
            foundStateMachineBehaviours.UnionWith(sm.behaviours);
            foreach(ChildAnimatorState cs in sm.states) {
                foundAnimatorStates.Add(cs.state);
                foundAnimatorStateTransitions.UnionWith(cs.state.transitions);
                foundStateMachineBehaviours.UnionWith(cs.state.behaviours);
            }
            foreach(ChildAnimatorStateMachine csm in sm.stateMachines) {
                if (foundAnimatorStateMachines.Add(csm.stateMachine)) {
                    // There is no property to access these. They're so easy to forget
                    // about that it seems even Unity forgets about these! Simply deleting
                    // a layer in the Animator GUI will leak these StateMachine transitions
                    // in the asset file.
                    foundAnimatorTransitions.UnionWith(sm.GetStateMachineTransitions(csm.stateMachine));
                    recursiveCollectAssets(csm.stateMachine);
                }
            }
        }

        foreach(AnimatorControllerLayer layer in controller.layers) {
            if (foundAnimatorStateMachines.Add(layer.stateMachine)) {
                recursiveCollectAssets(layer.stateMachine);
            }
        }

        var unusedAnimatorStateMachines      = originalAnimatorStateMachines.Except(foundAnimatorStateMachines).Cast<UnityEngine.Object>();
        var unusedAnimatorTransitions        = originalAnimatorTransitions.Except(foundAnimatorTransitions).Cast<UnityEngine.Object>();
        var unusedAnimatorStateTransitions   = originalAnimatorStateTransitions.Except(foundAnimatorStateTransitions).Cast<UnityEngine.Object>();
        var unusedAnimatorStates             = originalAnimatorStates.Except(foundAnimatorStates).Cast<UnityEngine.Object>();
        var unusedStateMachineBehaviours     = originalStateMachineBehaviours.Except(foundStateMachineBehaviours).Cast<UnityEngine.Object>();

        var unusedObjects = unusedAnimatorStateMachines
                    .Concat(unusedAnimatorTransitions)
                    .Concat(unusedAnimatorStateTransitions)
                    .Concat(unusedAnimatorStates)
                    .Concat(unusedStateMachineBehaviours);

        int num = 0;
        foreach(UnityEngine.Object o in unusedObjects) {
            AssetDatabase.RemoveObjectFromAsset(o);
            num++;
        }

        if (logNumRemoved && num > 0) {
            Debug.Log($"MGG: Removed {num} unused hidden (leaked) subassets in animator controller '{controller.name}'");
        }
    }
}

}