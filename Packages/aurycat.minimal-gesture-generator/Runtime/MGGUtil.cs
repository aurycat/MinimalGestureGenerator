using System;

#if UNITY_EDITOR
namespace aurycat.MGG
{

static public class MGGUtil
{
    [Flags]
    public enum StandardGestureFlag {
        // Neutral not included so it doesnt show in GUI. "All" and
        // "Everything" not included because they make Unity vomit
        // even thogh the docs show them working.
      //Neutral    = 1 << 0,
        Fist       = 1 << 1,
        HandOpen   = 1 << 2,
        Point      = 1 << 3,
        Victory    = 1 << 4,
        RockNRoll  = 1 << 5,
        FingerGun  = 1 << 6,
        ThumbsUp   = 1 << 7,
    }

    public const StandardGestureFlag StandardGestureFlagNeutral = (StandardGestureFlag)(1);
    public const StandardGestureFlag StandardGestureFlagNone = (StandardGestureFlag)(0);
    public const StandardGestureFlag StandardGestureFlagAll = (StandardGestureFlag)(~0);

    public enum ComboGestureMode {
        Asymmetric,
        Symmetric,
        SymmetricWithDoubles,
        LeftOnly,
        RightOnly,
    }

    public const int NumStandardGestures = 8; // Including Neutral
    static public readonly string[] StandardGestureNames = new string[NumStandardGestures] {
        "Neutral",
        "Fist",
        "HandOpen",
        "Point",
        "Victory",
        "RockNRoll",
        "FingerGun",
        "ThumbsUp",
    };

    // Asymmetric Combo Gestures
    // Includes every permutation of left- and right-hand gestures
    // (e.g. LFist+RPoint is different from LPoint+RFist)
    // The array is left-major order, i.e. L0R0, L0R1, L0R2, ..., L1R0, L1R1, L1R2, ..., L7R6, L7R7
    // This is the largest set of gestures possible, so is also the
    // length of the Motion[] array used to store gesture animations.
    public const int NumAsymmetricComboGestures = NumStandardGestures * NumStandardGestures; // 64
    static public readonly string[] AsymmetricComboGestureNames = new string[NumAsymmetricComboGestures];
    // Indicates which standard/base gestures each combo gesture is using.
    static public readonly StandardGestureFlag[] AsymmetricComboGestureUses = new StandardGestureFlag[NumAsymmetricComboGestures];

    // Symmetric Combo Gestures
    // Includes every combination of left- and right-hand gestures
    // (e.g. LFist+RPoint is the same as LPoint+RFist, and is named just "Fist + Point")
    public const int NumSymmetricComboGestures = (NumStandardGestures * (NumStandardGestures-1)/2) + 1; // 29
    static public readonly string[] SymmetricComboGestureNames = new string[NumSymmetricComboGestures];
    static public readonly int[] SymmetricToAsymmetricMap = new int[NumSymmetricComboGestures];
    // The Asymmetric->... maps are not needed so they're commented out here and in the initialization code.
    //static public readonly int[] AsymmetricToSymmetricMap = new int[NumAsymmetricComboGestures];
    // I don't know what a good name is for this map. It's used by RemapComboGestureArrayUsingMode.
    static public readonly int[] SymAsymMap2 = new int[NumAsymmetricComboGestures];
    static public readonly StandardGestureFlag[] SymmetricComboGestureUses = new StandardGestureFlag[NumSymmetricComboGestures];

    // Symmetric Combo Gestures with Doubles
    // Like above but supports a different motion for same gesture in both hands
    // (e.g. Fist + Fist)
    public const int NumSymmetricDoublesComboGestures = NumStandardGestures * (NumStandardGestures+1)/2; // 36
    static public readonly string[] SymmetricDoublesComboGestureNames = new string[NumSymmetricDoublesComboGestures];
    static public readonly int[] SymmetricDoublesToAsymmetricMap = new int[NumSymmetricDoublesComboGestures];
    //static public readonly int[] AsymmetricToSymmetricDoublesMap = new int[NumAsymmetricComboGestures];
    static public readonly int[] DoubleSymAsymMap2 = new int[NumAsymmetricComboGestures];
    static public readonly StandardGestureFlag[] SymmetricDoublesComboGestureUses = new StandardGestureFlag[NumSymmetricDoublesComboGestures];

    // One Hand Only
    static public readonly string[] LeftHandOnlyGestureNames = new string[NumStandardGestures];
    static public readonly string[] RightHandOnlyGestureNames = new string[NumStandardGestures];
    static public readonly int[] OneHandOnlyToAsymmetricMap = new int[NumStandardGestures];
    static public readonly int[] LeftHandOnlyAsymMap2 = new int[NumAsymmetricComboGestures];
    static public readonly int[] RightHandOnlyAsymMap2 = new int[NumAsymmetricComboGestures];
    static public readonly StandardGestureFlag[] OneHandOnlyGestureUses = new StandardGestureFlag[NumStandardGestures];

    static MGGUtil() {
        // Init Asymmetric
        for (int i = 0, c = 0; i < NumStandardGestures; i++) {
            for (int j = 0; j < NumStandardGestures; j++) {
                AsymmetricComboGestureNames[c] =
                    $"L{StandardGestureNames[i]} + R{StandardGestureNames[j]}";
                AsymmetricComboGestureUses[c] =
                    (StandardGestureFlag)((1 << i) | (1 << j)) & (~StandardGestureFlagNeutral);
                c++;
            }
        }

        // Init Symmetric
        SymmetricComboGestureNames[0] = StandardGestureNames[0];
        for (int i = 0, c = 1; i < NumStandardGestures; i++) {
            for (int j = i; j < NumStandardGestures; j++) {
                if (i == j) {
                    //AsymmetricToSymmetricMap[i*NumStandardGestures + j] = i;
                    SymAsymMap2[i*NumStandardGestures + j] = i;
                    continue;
                }
                SymAsymMap2[i*NumStandardGestures + j] = i*NumStandardGestures + j;
                SymAsymMap2[j*NumStandardGestures + i] = i*NumStandardGestures + j;
                //AsymmetricToSymmetricMap[i*NumStandardGestures + j] = c;
                //AsymmetricToSymmetricMap[j*NumStandardGestures + i] = c;
                SymmetricToAsymmetricMap[c] = i*NumStandardGestures + j;
                SymmetricComboGestureNames[c] =
                    (i == 0) ? $"Only {StandardGestureNames[j]}"
                             : $"{StandardGestureNames[i]} + {StandardGestureNames[j]}";
                SymmetricComboGestureUses[c] =
                    (StandardGestureFlag)((1 << i) | (1 << j));
                c++;
            }
        }

        // Init Symmetric with Doubles
        for (int i = 0, c = 0; i < NumStandardGestures; i++) {
            for (int j = i; j < NumStandardGestures; j++) {
                DoubleSymAsymMap2[i*NumStandardGestures + j] = i*NumStandardGestures + j;
                //AsymmetricToSymmetricDoublesMap[i*NumStandardGestures + j] = c;
                if (i != j) {
                    DoubleSymAsymMap2[j*NumStandardGestures + i] = i*NumStandardGestures + j;
                    //AsymmetricToSymmetricDoublesMap[j*NumStandardGestures + i] = c;
                }
                SymmetricDoublesToAsymmetricMap[c] = i*NumStandardGestures + j;
                SymmetricDoublesComboGestureNames[c] =
                    (c == 0) ? StandardGestureNames[0] :
                    (i == 0) ? $"Only one {StandardGestureNames[j]}"
                             : $"{StandardGestureNames[i]} + {StandardGestureNames[j]}";
                SymmetricDoublesComboGestureUses[c] =
                    (StandardGestureFlag)((1 << i) | (1 << j));
                c++;
            }
        }

        // One Hand Only
        for (int i = 0; i < NumStandardGestures; i++) {
            LeftHandOnlyGestureNames[i] = $"Left {StandardGestureNames[i]}";
            RightHandOnlyGestureNames[i] = $"Right {StandardGestureNames[i]}";
            OneHandOnlyToAsymmetricMap[i] = i;
            OneHandOnlyGestureUses[i] = (StandardGestureFlag)(1 << i);
            for (int j = 0; j < NumStandardGestures; j++) {
                LeftHandOnlyAsymMap2[i*NumStandardGestures + j] = i;
                RightHandOnlyAsymMap2[i*NumStandardGestures + j] = j;
            }
        }
    }

    static public bool GestureIsEnabled(StandardGestureFlag uses, StandardGestureFlag enabledMask)
    {
        uses &= (~StandardGestureFlagNeutral); // Mask out Neutral, it is always enabled
        return (uses & enabledMask) == uses;
    }

    // Takes a full-length (NumAsymmetricComboGestures) combo gesture array
    // containing elements for a particular gesture mode, and remaps it to a
    // an Asymmetric-mode array. E.g., it will duplicate elements as necessary
    // to fill in the full asymmetric gesture list from a symmetric gesture
    // list where only one side of the square "gesture grid" is filled in.
    static public T[] RemapComboGestureArrayUsingMode<T>(T[] arr, ComboGestureMode mode)
    {
        if (arr.Length != NumAsymmetricComboGestures) {
            throw new Exception($"Invalid length for combo gesture array {arr.Length}");
        }

        int[] map = null;

        switch(mode) {
        case ComboGestureMode.Asymmetric:
            return((T[])arr.Clone());
        case ComboGestureMode.Symmetric:
            map = SymAsymMap2;
            break;
        case ComboGestureMode.SymmetricWithDoubles:
            map = DoubleSymAsymMap2;
            break;
        case ComboGestureMode.LeftOnly:
            map = LeftHandOnlyAsymMap2;
            break;
        case ComboGestureMode.RightOnly:
            map = RightHandOnlyAsymMap2;
            break;
        default:
            throw new Exception($"Invalid ComboGestureMode {mode}");
        }

        T[] newarr = new T[NumAsymmetricComboGestures];
        for (int i = 0; i < NumAsymmetricComboGestures; i++) {
            newarr[i] = arr[map[i]];
        }
        return newarr;
    }
}

}
#endif