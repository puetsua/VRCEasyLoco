using System.Runtime.CompilerServices;

// The package's types are internal - nothing outside it is meant to call them. The test assembly is
// the one exception. It started with the pieces that are pure logic - the pose-name rules and the
// menu-value maths - and has since reached further in, to the blend-tree copy and to the motion
// replacement itself. Those two do touch the AssetDatabase, and that is the point: they are where
// a mistake writes into a shared package asset or into the wrong locomotion branch, which is not
// something a test can reach from outside. So the rule is what a test needs to pin, not only what
// happens to be free of Editor state.
[assembly: InternalsVisibleTo("Puetsua.VRCEasyLoco.Editor.Tests")]
