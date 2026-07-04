#if VISTA
using UnityEngine;
using UnityEngine.UIElements;

namespace Pinwheel.VistaEditor.UIElements
{
    public class VerticalSeparator : VisualElement
    {
        public VerticalSeparator()
        {
            style.width = new StyleLength(1);
            style.height = new StyleLength(new Length(70, LengthUnit.Percent));
            style.marginLeft = new StyleLength(3);
            style.marginRight = new StyleLength(3);
            style.backgroundColor = new StyleColor(new Color32(50, 50, 50, 255));
            style.alignSelf = new StyleEnum<Align>(Align.Center);
        }
    }
}
#endif
