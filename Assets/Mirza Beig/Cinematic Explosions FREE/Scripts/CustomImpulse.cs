using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Unity.Cinemachine;   // Cinemachine 3.x renombró el namespace (en 2.x era 'Cinemachine')

namespace MirzaBeig.CinematicExplosionsFree
{
    public class CustomImpulse : MonoBehaviour
    {
        CinemachineImpulseSource source;

        void Start()
        {

        }

        void OnEnable()
        {
            if (!source)
            {
                source = GetComponent<CinemachineImpulseSource>();
            }

            source.GenerateImpulse();
        }

        void Update()
        {

        }
    }
}
