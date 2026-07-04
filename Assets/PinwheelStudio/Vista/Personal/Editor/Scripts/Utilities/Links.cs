#if VISTA
using System;
using System.Text;

namespace Pinwheel.VistaEditor
{
    public static class Links
    {
        public static string CONTACT_PAGE = "https://pinwheelstud.io/contact";
        public static string SUPPORT_EMAIL = "pinwheel.customer@gmail.com";
        public static string DISCORD = "https://discord.gg/D4VehsCQXb";
        public static string DOC = "https://docs.pinwheelstud.io/vista";
        public static string YOUTUBE = "https://www.youtube.com/channel/UCebwuk5CfIe5kolBI9nuBTg";
        public static string FACEBOOK = "https://www.facebook.com/polaris.terrain";

        public static string PINWHEEL_PUBLISHER = "https://assetstore.unity.com/publishers/17305";
        public static string STORE_PAGE = "https://u3d.as/332m";

        public static string VEGETATION_ASSETS = "https://api.pinwheelstud.io/aff/vegetation-assets";
        public static string TEXTURE_ASSETS = "https://api.pinwheelstud.io/aff/texture-assets";
        public static string PROPS_ASSETS = "https://api.pinwheelstud.io/aff/props-assets";

        public static string VISTA_PRO = "https://assetstore.unity.com/packages/tools/terrain/procedural-terrain-hexmap-vista-pro-264414";

        public static string GetDefaultNodeDocumentationUrl(Type nodeType)
        {
            string typeName = nodeType.Name;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < typeName.Length; i++)
            {
                char c = typeName[i];
                if (i > 0 && char.IsUpper(c))
                {
                    sb.Append('-');
                }
                sb.Append(char.ToLower(c));
            }
            return $"{DOC}/docs/nodes-reference-{sb}.html";
        }
    }
}
#endif
