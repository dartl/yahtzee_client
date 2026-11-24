using UnityEngine;

namespace Yahtzee.Core
{
    /// <summary>
    /// Phones and tablets stay portrait-only. Landscape gameplay is for PC / editor / desktop Web.
    /// </summary>
    public static class AppOrientation
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
#if UNITY_ANDROID || UNITY_IOS
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.orientation = ScreenOrientation.Portrait;
#endif
        }
    }
}
