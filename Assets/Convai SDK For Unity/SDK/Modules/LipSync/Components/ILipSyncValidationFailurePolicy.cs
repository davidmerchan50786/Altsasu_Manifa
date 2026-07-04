using UnityEngine;

namespace Convai.Modules.LipSync
{
    internal interface ILipSyncValidationFailurePolicy
    {
        public bool Apply(Component context, LipSyncValidationResult result);
    }
}
