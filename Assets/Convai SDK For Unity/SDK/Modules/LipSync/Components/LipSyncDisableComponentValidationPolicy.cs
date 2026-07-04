using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using UnityEngine;

namespace Convai.Modules.LipSync
{
    internal sealed class LipSyncDisableComponentValidationPolicy : ILipSyncValidationFailurePolicy
    {
        public bool Apply(Component context, LipSyncValidationResult result)
        {
            if (result.IsValid) return true;

            ConvaiLogger.Error(result.Message, LogCategory.LipSync);
            if (context is Behaviour behaviour) behaviour.enabled = false;

            return false;
        }
    }
}
