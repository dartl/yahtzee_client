using UnityEngine;

namespace Yahtzee.UI
{
    public abstract class UIScreen : MonoBehaviour
    {
        public abstract ScreenType Type { get; }

        public virtual void Show()
        {
            gameObject.SetActive(true);
        }

        public virtual void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
