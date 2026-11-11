using LayoutRecommender.Core;
using UnityEngine;

namespace LayoutRecommender.Screens
{
    /// <summary>화면 하나. AppManager가 루트 RectTransform을 만들어 Build를 호출한다.</summary>
    public abstract class ScreenBase : MonoBehaviour
    {
        protected AppManager App => AppManager.Instance;

        public abstract void Build(RectTransform root);
    }
}
