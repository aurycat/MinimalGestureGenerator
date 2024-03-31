# Minimal Gesture Generator (MGG)

A tool to automate generating an animator controller layer for a VRChat avatar's expressions & gestures.

Compare MGG to [ComboGestureExpressions](https://docs.hai-vr.dev/docs/products/combo-gesture-expressions) or [FaceEmo](https://suzuryg.github.io/face-emo/). MGG is not as fully-featured as these other two options, omitting features such as multiple "mood sets", gesture weight, and a native GUI to generate expression .anim files. Hence the name "Minimal"! However, MGG creates only a single animator layer which is more optimized and easier to work with manually if necessary. MGG also supports "manual overrides" which allow you to force a particular expression using your radial menu -- great for photos, or if you just aren't good at memorizing hand gestures!


## Installation

MGG is available as a VRChat Creator Companion VPM package.

1. MGG depends on Haï~'s Animator As Code package. Install their VPM listing: https://hai-vr.github.io/vpm-listing
2. Install my VPM listing: https://aurycat.github.io/vpm
3. In your avatar project, install the Minimal Gesture Generator package

You can also view all MGG releases [here](https://github.com/aurycat/MinimalGestureGenerator/releases).


## Usage overview

1. Install MGG through the VRC Creator Companion.
2. In your avatar project, select the "Tools > Add MinimalGestureGenerator Object" menu option.
3. In the Inspector window for the created MGG object, ...
4. ... drag in your avatar root GameObject, and your FX animator controller asset,
5. ... setup your desired [hand gestures](#hand-gesture-expressions), [contact expressions](#contact-expressions), and [manual override expressions](#manual-override-expressions) (see sections below),
6. ... set any other necessary settings (e.g. blinking, transition duration),
7. ... press "Generate Animator Layer" at the top!

⚠️ All the properties in the Inspector have tooltips (extra info on mouse hover). Please read those for the most info on any specific option!


## Making Expression Anim Clips

Every expression will be a `.anim` clip asset or a BlendTree asset. `.anim` clips can be created manually or with a tool such as [Haï~'s Visual Expression Editor](https://docs.hai-vr.dev/docs/products/visual-expressions-editor) (Haï~'s tool highly recommended!). BlendTree assets can be created easily with the "Create new BlendTree asset" at the bottom of MGG inspectors.

BlendTrees are useful to create any expression with some sort of dynamic capability. For example, even though MGG does not directly support using [`GestureLeftWeight` or `GestureRightWeight`](https://creators.vrchat.com/avatars/animator-parameters/#parameters), you can make an expression be a BlendTree which blends based on those parameters.


## Expression Types

### Hand gesture expressions

Expressions driven by VRC's built-in `GestureLeft` and `GestureRight` parameters.

"Combo Gesture Mode" lets you choose...
- **Symmetric:** Left and right hand gestures are equivalent. You can set combo gestures, e.g. `Left Victory + Right ThumbsUp`, but the opposite combo, `Left ThumbsUp + Right Victory` will automatically be set to the same thing.
- **Symmetric With Doubles:** Same as Symmetric, but also includes "Both" options. For example, you can assign a separate expression to both hands having `Victory` simultaneously.
- **Asymmetric:** This is the most powerful (but most annoying to configure!) option. Every permutation of left & right hand gestures can be a different expression. For example, you can have two different expressions for `Left Victory + Right ThumbsUp` and `Left ThumbsUp + Right Victory`.

(Note that the only purpose of "Combo Gesture Mode" is to make setting up your animations easier. Anything made with "Symmetric" or "Symmetric With Doubles" can be implemented in "Asymmetric".)

The "Enabled Hand Gestures" dropdown allows you to mark specific hand gestures as disabled, which means they act as if they didn't exist and will be treated as neutral if used. For example, if you never want to use `HandOpen`, you can uncheck it from that menu, and the generated animator controller will treat `HandOpen` as if it were the neutral gesture. This lets the animator controller have fewer states, so I suggest disabling any gestures you don't use. Disabling a gesture also removes the entries from the long Motion property list, which makes it easier to read.


### Contact expressions

Contact expressions respond to float parameters. The intended purpose is to respond to [VRC Contact Receivers](https://creators.vrchat.com/avatars/avatar-dynamics/contacts/#vrccontactreceiver), but any float parameter works. If the float parameter is greater than 0, the gesture is active.

By default, contact expressions take priority over hand gesture expressions. In another words, if a contact expression is active, your current hand gesture will be ignored. However, you can click the cyan up-arrow button to make the contact expression "low-priority", which means that it will only activate if the current hand gesture is neutral.

By default, a contact expression is only "on" (parameter > 0) or "off" (parameter = 0), without blending using the value of the parameter. You can click the BlendTree icon next to a contact expression to automatically turn the contact expression into a BlendTree which blends between neutral and the given animation clip based on the percent value of the parameter. You can also manually provide a BlendTree as the Motion, if you want something more specific.


### Manual override expressions

Manual override expressions allow you to use a single integer parameter to override all other types of expressions. These are perfect for controlling your expression with radial menu options. If the chosen integer parameter is 0, manual overrides are off and hand gestures/contact expressions are used. If the integer parameter is not 0, the manual override expression with that number will be active.


## Non-destructive editing?

A lot of avatar development has shifted to non-destructive methods using tools like VRC Fury. MGG does not directly follow those methods. While non-destructive editing is very useful for quick development and swapping out features, it is more difficult to create optimized avatars.

However, an easy way to make MGG work non-destructively is by setting the "FX Controller" property to a new, empty animator controller asset instead of your avatar's FX controller directly. Generate expressions into that new controller. Then, you can use VRC Fury / Modular Avatar / etc to combine that new controller into your primary FX controller on-demand.


## Credits

Created by aurycat.

This tool is released under the [MIT license](https://mit-license.org/).