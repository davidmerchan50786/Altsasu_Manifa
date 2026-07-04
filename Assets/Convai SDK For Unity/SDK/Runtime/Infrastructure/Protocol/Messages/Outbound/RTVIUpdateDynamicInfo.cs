namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Outbound dynamic info update message.</summary>
    public class RTVIUpdateDynamicInfo : RTVISendMessageBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="RTVIUpdateDynamicInfo" /> class.
        /// </summary>
        /// <param name="dynamicInfo">Dynamic info payload.</param>
        public RTVIUpdateDynamicInfo(DynamicInfo dynamicInfo)
        {
            Type = "update-dynamic-info";
            Data = new { dynamic_info = dynamicInfo };
        }
    }
}
