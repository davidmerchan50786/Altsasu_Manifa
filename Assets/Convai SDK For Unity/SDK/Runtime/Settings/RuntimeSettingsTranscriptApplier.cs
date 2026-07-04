using System;
using Convai.Runtime.Presentation.Services;
using Convai.Shared.Abstractions;
using Convai.Shared.Types;

namespace Convai.Runtime.Settings
{
    /// <summary>
    ///     Applies transcript-related runtime settings to transcript presentation services.
    /// </summary>
    internal sealed class RuntimeSettingsTranscriptApplier : IDisposable
    {
        private readonly TranscriptUIController _controller;
        private readonly IConvaiRuntimeSettingsService _settings;

        public RuntimeSettingsTranscriptApplier(IConvaiRuntimeSettingsService settings,
            TranscriptUIController controller)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));

            _settings.Changed += OnSettingsChanged;
            Apply(_settings.Current);
        }

        public void Dispose() => _settings.Changed -= OnSettingsChanged;

        private void OnSettingsChanged(ConvaiRuntimeSettingsChanged changed)
        {
            if ((changed.Mask &
                 (ConvaiRuntimeSettingsChangeMask.TranscriptEnabled |
                  ConvaiRuntimeSettingsChangeMask.TranscriptMode)) == 0)
                return;

            Apply(changed.Current);
        }

        private void Apply(ConvaiRuntimeSettingsSnapshot snapshot)
        {
            _controller.SetEnabled(snapshot.TranscriptEnabled);

            int modeIndex = snapshot.TranscriptMode switch
            {
                ConvaiTranscriptMode.Subtitle => 1,
                ConvaiTranscriptMode.QuestionAnswer => 2,
                _ => 0
            };

            _controller.SetModeByIndex(modeIndex);
        }
    }
}
