using System;
using Convai.Runtime.Components;
using Convai.Runtime.Presentation.Services;
using Convai.Shared.Abstractions;
using Convai.Shared.Types;

namespace Convai.Runtime.Settings
{
    /// <summary>
    ///     Applies player display-name runtime settings to the active player component.
    /// </summary>
    internal sealed class RuntimeSettingsPlayerIdentityApplier : IDisposable
    {
        private readonly IPlayerInputService _playerInputService;
        private readonly IConvaiRuntimeSettingsService _settings;

        public RuntimeSettingsPlayerIdentityApplier(
            IConvaiRuntimeSettingsService settings,
            IPlayerInputService playerInputService)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _playerInputService = playerInputService ?? throw new ArgumentNullException(nameof(playerInputService));

            _settings.Changed += OnSettingsChanged;
            Apply(_settings.Current);
        }

        public void Dispose() => _settings.Changed -= OnSettingsChanged;

        private void OnSettingsChanged(ConvaiRuntimeSettingsChanged changed)
        {
            if ((changed.Mask & ConvaiRuntimeSettingsChangeMask.PlayerDisplayName) == 0) return;

            Apply(changed.Current);
        }

        private void Apply(ConvaiRuntimeSettingsSnapshot snapshot)
        {
            if (_playerInputService?.Player is ConvaiPlayer player)
                player.SetRuntimeDisplayName(snapshot.PlayerDisplayName);
        }
    }
}
