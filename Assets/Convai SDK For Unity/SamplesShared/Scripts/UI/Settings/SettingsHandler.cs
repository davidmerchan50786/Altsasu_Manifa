using System;
using System.Collections.Generic;
using Convai.Runtime;
using Convai.Runtime.Persistence;
using Convai.Runtime.Presentation.Services;
using Convai.Runtime.Presentation.Views.Settings;
using Convai.Runtime.Settings;
using Convai.Shared.Abstractions;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Sample.UI.Settings
{
    /// <summary>
    ///     Sample settings handler that spawns and injects the settings panel view.
    /// </summary>
    public class SettingsHandler : MonoBehaviour
    {
        [SerializeField] private SettingsPanel settingsPanelPrefab;

        private IMicrophoneDeviceService _microphoneDeviceService;

        private SettingsPanel _panel;

        private IConvaiSettingsPanelController _panelController;
        private bool _panelInjected;
        private IConvaiRuntimeSettingsService _runtimeSettingsService;

        private void Awake()
        {
            if (settingsPanelPrefab == null)
            {
                Debug.LogWarning("[SettingsHandler] Settings panel prefab is not assigned.");
                return;
            }

            _panel = Instantiate(settingsPanelPrefab, transform);

            if (!TryInitialize())
            {
                EnsureFallbackServices();
                TryInitialize();
            }
        }

        public void Inject(
            IConvaiSettingsPanelController panelController = null,
            IConvaiRuntimeSettingsService runtimeSettingsService = null,
            IMicrophoneDeviceService microphoneDeviceService = null)
        {
            _panelController = panelController;
            _runtimeSettingsService = runtimeSettingsService;
            _microphoneDeviceService = microphoneDeviceService;
            TryInitialize();
        }

        private bool TryInitialize()
        {
            if (_panel == null ||
                _panelController == null ||
                _runtimeSettingsService == null ||
                _microphoneDeviceService == null)
                return false;

            if (!_panelInjected)
            {
                _panel.Inject(_panelController, _runtimeSettingsService, _microphoneDeviceService);
                _panelInjected = true;
            }

            return true;
        }

        private void EnsureFallbackServices()
        {
            _panelController ??= new ConvaiSettingsPanelController();
            _microphoneDeviceService ??= new SampleMicrophoneDeviceService();
            _runtimeSettingsService ??= new ConvaiRuntimeSettingsService(
                ConvaiSettings.Instance,
                new ConvaiRuntimeSettingsStore(new PlayerPrefsKeyValueStore()),
                _microphoneDeviceService);
        }

        public void TogglePanel() => _panelController?.Toggle();

        private sealed class SampleMicrophoneDeviceService : IMicrophoneDeviceService
        {
            public IReadOnlyList<ConvaiMicrophoneDevice> GetAvailableDevices()
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return Array.Empty<ConvaiMicrophoneDevice>();
#else
                string[] names = Microphone.devices ?? Array.Empty<string>();
                if (names.Length == 0) return Array.Empty<ConvaiMicrophoneDevice>();

                var idsByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var devices = new List<ConvaiMicrophoneDevice>(names.Length);

                for (int i = 0; i < names.Length; i++)
                {
                    string displayName = string.IsNullOrWhiteSpace(names[i]) ? $"Microphone {i + 1}" : names[i].Trim();
                    if (!idsByName.TryGetValue(displayName, out int count)) count = 0;

                    count++;
                    idsByName[displayName] = count;

                    string id = count == 1 ? displayName : $"{displayName}#{count}";
                    devices.Add(new ConvaiMicrophoneDevice(id, displayName, i));
                }

                return devices;
#endif
            }

            public ConvaiMicrophoneDevice ResolvePreferredDevice(string preferredDeviceId)
            {
                IReadOnlyList<ConvaiMicrophoneDevice> devices = GetAvailableDevices();
                if (devices.Count == 0) return ConvaiMicrophoneDevice.None;

                if (TryResolveDeviceId(preferredDeviceId, out ConvaiMicrophoneDevice resolved))
                    return resolved;

                return devices[0];
            }

            public string ResolvePreferredDeviceId(string preferredDeviceId) =>
                ResolvePreferredDevice(preferredDeviceId).Id;

            public int ResolvePreferredDeviceIndex(string preferredDeviceId) =>
                ResolvePreferredDevice(preferredDeviceId).Index;

            public bool TryResolveDeviceId(string deviceId, out ConvaiMicrophoneDevice device)
            {
                IReadOnlyList<ConvaiMicrophoneDevice> devices = GetAvailableDevices();
                device = ConvaiMicrophoneDevice.None;

                if (devices.Count == 0 || string.IsNullOrWhiteSpace(deviceId)) return false;

                for (int i = 0; i < devices.Count; i++)
                {
                    if (string.Equals(devices[i].Id, deviceId, StringComparison.Ordinal))
                    {
                        device = devices[i];
                        return true;
                    }
                }

                for (int i = 0; i < devices.Count; i++)
                {
                    if (string.Equals(devices[i].Id, deviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        device = devices[i];
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
