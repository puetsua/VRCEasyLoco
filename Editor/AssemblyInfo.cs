using System.Runtime.CompilerServices;

// The package's types are internal - nothing outside it is meant to call them. The test assembly is
// the one exception: it exercises the pose-name rules and the menu-value maths directly, which is
// the whole point of keeping those two pieces free of Editor and AssetDatabase state.
[assembly: InternalsVisibleTo("Puetsua.VRCEasyLoco.Editor.Tests")]
