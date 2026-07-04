#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pinwheel.Vista;
using Pinwheel.Vista.Graphics;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Pinwheel.Vista.BigWorld
{
    public static class GetBiomesHandler
    {
#if UNITY_EDITOR
        [InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        private static void OnInitialize()
        {
            VistaManager.getBiomesCallback += OnGetBiomes;
        }

        private static IBiome[] OnGetBiomes(VistaManager vm)
        {
            Collector<IBiome> biomeCollector = new Collector<IBiome>();
            IBiome[] childrenBiomes = vm.GetComponentsInChildren<IBiome>();
            foreach (IBiome b in childrenBiomes)
            {
                MonoBehaviour monoBehaviour = b as MonoBehaviour;
                if (monoBehaviour != null && !monoBehaviour.isActiveAndEnabled)
                {
                    continue;
                }
                biomeCollector.Add(b);
            }

            VistaManager.collectFreeBiomes?.Invoke(vm, biomeCollector);
            IBiome[] biomes = biomeCollector.ToArray();

            for (int i = 0; i < biomes.Length - 1; ++i)
            {
                for (int j = i + 1; j < biomes.Length; ++j)
                {
                    if (biomes[i].order > biomes[j].order)
                    {
                        Utilities.Swap(ref biomes, i, j);
                    }
                }
            }

            return biomes;
        }
    }
}
#endif
