namespace Convai.Modules.LipSync
{
    internal enum LipSyncValidationFailure : byte
    {
        None = 0,
        ProfileMissing,
        CharacterBindingMissing,
        CharacterIdMissing
    }

    internal readonly struct LipSyncValidationResult
    {
        public LipSyncValidationFailure Failure { get; }
        public string Message { get; }
        public bool IsValid => Failure == LipSyncValidationFailure.None;

        private LipSyncValidationResult(LipSyncValidationFailure failure, string message)
        {
            Failure = failure;
            Message = message;
        }

        public static LipSyncValidationResult Valid() => new(LipSyncValidationFailure.None, string.Empty);

        public static LipSyncValidationResult Invalid(LipSyncValidationFailure failure, string message) =>
            new(failure, message ?? string.Empty);
    }
}
