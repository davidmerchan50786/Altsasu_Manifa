#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using Pinwheel.Vista;

namespace Pinwheel.VistaEditor
{
    [CustomEditor(typeof(LPBAdditionalData))]
    public class LPBAdditionalDataInspector : Editor
    {
        private LPBAdditionalData m_instance;
        private void OnEnable()
        {
            m_instance = target as LPBAdditionalData;
        }
        
        public override void OnInspectorGUI()
        {
        }
    }
}
#endif
