using Convai.Domain.Models.LipSync;
using Convai.Shared.Interfaces;
using UnityEngine;

namespace Convai.Modules.LipSync
{
    internal interface ILipSyncLifecycleValidator
    {
        public LipSyncValidationResult ValidateProfile(LipSyncProfileId profileId);

        public LipSyncValidationResult ValidateCharacterBinding(
            Component context,
            ref ICharacterIdentitySource identitySource,
            out string characterId);
    }
}
