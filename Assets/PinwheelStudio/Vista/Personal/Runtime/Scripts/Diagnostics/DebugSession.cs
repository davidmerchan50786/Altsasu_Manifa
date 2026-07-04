#if VISTA
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Pinwheel.Vista.Diagnostics
{
    [Serializable]
    public class HardwareSnapshot
    {
        public string graphicsDeviceName;
        public string graphicsDeviceType;
        public string graphicsDeviceVendor;
        public string graphicsDeviceVersion;
        public int graphicsMemorySizeMb;
        public int graphicsShaderLevel;
        public bool graphicsMultiThreaded;
        public string operatingSystem;
        public string processorType;
        public int processorCount;
        public int systemMemorySizeMb;
        public string unityVersion;

        public static HardwareSnapshot Capture()
        {
            HardwareSnapshot snapshot = new HardwareSnapshot();
            snapshot.graphicsDeviceName = SystemInfo.graphicsDeviceName;
            snapshot.graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString();
            snapshot.graphicsDeviceVendor = SystemInfo.graphicsDeviceVendor;
            snapshot.graphicsDeviceVersion = SystemInfo.graphicsDeviceVersion;
            snapshot.graphicsMemorySizeMb = SystemInfo.graphicsMemorySize;
            snapshot.graphicsShaderLevel = SystemInfo.graphicsShaderLevel;
            snapshot.graphicsMultiThreaded = SystemInfo.graphicsMultiThreaded;
            snapshot.operatingSystem = SystemInfo.operatingSystem;
            snapshot.processorType = SystemInfo.processorType;
            snapshot.processorCount = SystemInfo.processorCount;
            snapshot.systemMemorySizeMb = SystemInfo.systemMemorySize;
            snapshot.unityVersion = Application.unityVersion;
            return snapshot;
        }
    }

    [Serializable]
    public class SeedSnapshot
    {
        public string label;
        public int seed;
    }

    [Serializable]
    public class DebugSession
    {
        public string sessionId;
        public string startTime;
        public HardwareSnapshot hardware;
        public List<SeedSnapshot> seeds = new List<SeedSnapshot>();
        public List<DebugEvent> events = new List<DebugEvent>();

        [NonSerialized]
        public int textureFileCount;

        [NonSerialized]
        public int bufferFileCount;

        [NonSerialized]
        private string m_sessionDirectory;

        public string sessionDirectory
        {
            get
            {
                return m_sessionDirectory;
            }
        }

        public static DebugSession Create()
        {
            DebugSession session = new DebugSession();
            session.sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
            session.startTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            session.hardware = HardwareSnapshot.Capture();
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            session.m_sessionDirectory = Path.Combine(
                Application.persistentDataPath,
                "VistaDebug",
                $"{timestamp}_{session.sessionId}");
            return session;
        }

        public void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(m_sessionDirectory);
            Directory.CreateDirectory(Path.Combine(m_sessionDirectory, "textures"));
            Directory.CreateDirectory(Path.Combine(m_sessionDirectory, "buffers"));
        }

        public string GetTextureFilePath(int index)
        {
            return Path.Combine(m_sessionDirectory, "textures", $"{index}.raw");
        }

        public string GetBufferFilePath(int index)
        {
            return Path.Combine(m_sessionDirectory, "buffers", $"{index}.bin");
        }

        public void WriteSessionJson()
        {
            string json = JsonUtility.ToJson(this, prettyPrint: true);
            string path = Path.Combine(m_sessionDirectory, "session.json");
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        public static DebugSession LoadFromDirectory(string directory)
        {
            string jsonPath = Path.Combine(directory, "session.json");
            if (!File.Exists(jsonPath))
            {
                return null;
            }
            string json = File.ReadAllText(jsonPath, Encoding.UTF8);
            DebugSession session = JsonUtility.FromJson<DebugSession>(json);
            session.m_sessionDirectory = directory;
            return session;
        }
    }
}
#endif
