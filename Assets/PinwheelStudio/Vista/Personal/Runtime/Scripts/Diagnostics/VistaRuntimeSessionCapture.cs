#if VISTA
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Pinwheel.Vista.Diagnostics
{
    [AddComponentMenu("Vista/Diagnostics/Runtime Session Capture")]
    [DisallowMultipleComponent]
    public class VistaRuntimeSessionCapture : MonoBehaviour
    {
        [SerializeField]
        private VistaManager m_vistaManager;

        [SerializeField]
        private Vector2 m_screenPosition = new Vector2(16f, 16f);

        [SerializeField]
        private float m_panelWidth = 360f;

        [SerializeField]
        private float m_buttonHeight = 32f;

        [SerializeField]
        private bool m_shuffleBiomeSeedsBeforeGenerating = false;

        private const int SHUFFLE_SEED_MIN = 0;
        private const int SHUFFLE_SEED_MAX = 10000;

        private bool m_isCapturing;
        private string m_lastSavedSessionPath;
        private string m_statusMessage;
        private readonly List<SeedSnapshot> m_lastSessionSeeds = new List<SeedSnapshot>();
        private GUIStyle m_pathStyle;

        private GUIStyle pathStyle
        {
            get
            {
                if (m_pathStyle == null)
                {
                    m_pathStyle = new GUIStyle(GUI.skin.label);
                    m_pathStyle.wordWrap = true;
                }

                return m_pathStyle;
            }
        }

        private void OnGUI()
        {
            Rect areaRect = new Rect(m_screenPosition.x, m_screenPosition.y, m_panelWidth, 420f);
            GUILayout.BeginArea(areaRect, GUI.skin.box);

            m_shuffleBiomeSeedsBeforeGenerating = GUILayout.Toggle(m_shuffleBiomeSeedsBeforeGenerating, "Shuffle biome seeds before generating");
            GUILayout.Space(6f);

            GUI.enabled = !m_isCapturing && !VistaDebugger.isRecording;
            if (GUILayout.Button("Capture Session", GUILayout.Height(m_buttonHeight)))
            {
                BeginCapture();
            }
            GUI.enabled = true;

            if (m_isCapturing || VistaDebugger.isRecording)
            {
                GUILayout.Space(6f);
                GUILayout.Label("Recording...");
            }

            if (!string.IsNullOrEmpty(m_statusMessage))
            {
                GUILayout.Space(6f);
                GUILayout.Label(m_statusMessage, pathStyle);
            }

            if (!string.IsNullOrEmpty(m_lastSavedSessionPath))
            {
                GUILayout.Space(6f);
                GUILayout.Label("Session Path", pathStyle);
                GUILayout.Label(m_lastSavedSessionPath, pathStyle);

                GUI.enabled = !m_isCapturing;
                if (GUILayout.Button("Open Folder"))
                {
                    OpenRecordedFolder();
                }
                GUI.enabled = true;
            }

            if (m_lastSessionSeeds.Count > 0)
            {
                GUILayout.Space(6f);
                GUILayout.Label("Biome Seeds", pathStyle);
                for (int i = 0; i < m_lastSessionSeeds.Count; ++i)
                {
                    SeedSnapshot seedSnapshot = m_lastSessionSeeds[i];
                    GUILayout.Label($"{seedSnapshot.label}: {seedSnapshot.seed}", pathStyle);
                }
            }

            GUILayout.EndArea();
        }

        private void BeginCapture()
        {
            m_statusMessage = null;
            m_lastSavedSessionPath = null;
            m_lastSessionSeeds.Clear();

            VistaManager vistaManager = ResolveVistaManager();
            if (vistaManager == null)
            {
                m_statusMessage = "No VistaManager found in the scene.";
                Debug.LogWarning("[VistaRuntimeSessionCapture] No VistaManager found in the scene.", this);
                return;
            }

            StartCoroutine(CaptureSession(vistaManager));
        }

        private VistaManager ResolveVistaManager()
        {
            if (m_vistaManager != null)
            {
                return m_vistaManager;
            }

            m_vistaManager = UnityEngine.Object.FindFirstObjectByType<VistaManager>();
            return m_vistaManager;
        }

        private IEnumerator CaptureSession(VistaManager vistaManager)
        {
            m_isCapturing = true;
            string savedSessionPath = null;
            bool sessionStarted = false;
            List<SeedSnapshot> sessionSeeds = PrepareBiomeSeeds(vistaManager, m_shuffleBiomeSeedsBeforeGenerating);

            try
            {
                VistaDebugger.BeginSession();
                sessionStarted = true;
                VistaDebugger.SetSessionSeeds(sessionSeeds);

                ProgressiveTask task = vistaManager.ForceGenerate();
                yield return task;
            }
            finally
            {
                if (sessionStarted && VistaDebugger.isRecording)
                {
                    savedSessionPath = VistaDebugger.EndSession();
                }

                m_isCapturing = false;
            }

            if (!string.IsNullOrEmpty(savedSessionPath))
            {
                m_lastSavedSessionPath = savedSessionPath;
                m_lastSessionSeeds.AddRange(sessionSeeds);
                Debug.Log($"[VistaRuntimeSessionCapture] Session saved to: {savedSessionPath}", this);
            }
        }

        private void OpenRecordedFolder()
        {
            if (string.IsNullOrEmpty(m_lastSavedSessionPath))
            {
                return;
            }

            if (!Directory.Exists(m_lastSavedSessionPath))
            {
                m_statusMessage = "Recorded session folder no longer exists.";
                Debug.LogWarning($"[VistaRuntimeSessionCapture] Session folder not found: {m_lastSavedSessionPath}", this);
                return;
            }

            string folderUri = new System.Uri(m_lastSavedSessionPath).AbsoluteUri;
            Application.OpenURL(folderUri);
        }

        private List<SeedSnapshot> PrepareBiomeSeeds(VistaManager vistaManager, bool shouldShuffle)
        {
            List<SeedSnapshot> sessionSeeds = new List<SeedSnapshot>();
            IBiome[] biomes = vistaManager.GetBiomes();
            if (biomes == null || biomes.Length == 0)
            {
                return sessionSeeds;
            }

            System.Random random = shouldShuffle ? new System.Random(unchecked(Environment.TickCount ^ GetInstanceID())) : null;
            HashSet<int> usedSeeds = shouldShuffle ? new HashSet<int>() : null;

            for (int i = 0; i < biomes.Length; ++i)
            {
                IBiome biome = biomes[i];
                if (biome == null)
                {
                    continue;
                }

                if (!TryGetSeedProperty(biome, out PropertyInfo seedProperty))
                {
                    continue;
                }

                int seedValue = (int)seedProperty.GetValue(biome, null);
                if (shouldShuffle)
                {
                    seedValue = NextUniqueSeed(random, usedSeeds);
                    seedProperty.SetValue(biome, seedValue, null);
                }

                SeedSnapshot seedSnapshot = new SeedSnapshot();
                seedSnapshot.label = GetBiomeLabel(biome);
                seedSnapshot.seed = seedValue;
                sessionSeeds.Add(seedSnapshot);
            }

            return sessionSeeds;
        }

        private static bool TryGetSeedProperty(IBiome biome, out PropertyInfo seedProperty)
        {
            seedProperty = biome.GetType().GetProperty("seed", BindingFlags.Instance | BindingFlags.Public);
            return seedProperty != null
                && seedProperty.PropertyType == typeof(int)
                && seedProperty.CanRead
                && seedProperty.CanWrite;
        }

        private static int NextUniqueSeed(System.Random random, HashSet<int> usedSeeds)
        {
            if (usedSeeds.Count > SHUFFLE_SEED_MAX - SHUFFLE_SEED_MIN)
            {
                return random.Next(SHUFFLE_SEED_MIN, SHUFFLE_SEED_MAX + 1);
            }

            int seedValue;
            do
            {
                seedValue = random.Next(SHUFFLE_SEED_MIN, SHUFFLE_SEED_MAX + 1);
            }
            while (!usedSeeds.Add(seedValue));

            return seedValue;
        }

        private static string GetBiomeLabel(IBiome biome)
        {
            if (biome == null || biome.gameObject == null)
            {
                return "Biome";
            }

            string hierarchyPath = GetHierarchyPath(biome.gameObject.transform);
            return $"{hierarchyPath} ({biome.GetType().Name})";
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                path = $"{current.name}/{path}";
                current = current.parent;
            }

            return path;
        }
    }
}
#endif
